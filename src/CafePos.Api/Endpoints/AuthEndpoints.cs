using CafePos.Api.Auth;
using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Support;
using CafePos.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CafePos.Api.Endpoints;

public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

    private static LoginResponse ToResponse(User u, TokenService tokens)
    {
        var (token, exp) = tokens.Create(u);
        return new LoginResponse(token, exp, u.Username, u.FullName, u.Role, u.MustChangePassword);
    }

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) =>
        {
            var username = (req.Username ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Active);
            if (user is null) return Results.Unauthorized();

            var now = DateTime.UtcNow;
            if (user.LockoutUntilUtc > now)
                return Results.Problem("Cuenta bloqueada temporalmente por intentos fallidos. Intenta más tarde.", statusCode: 423);

            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= MaxFailedLogins)
                {
                    user.LockoutUntilUtc = now.Add(LockoutTime);
                    user.FailedLoginCount = 0;
                }
                await db.SaveChangesAsync();
                return Results.Unauthorized();
            }

            user.FailedLoginCount = 0;
            user.LockoutUntilUtc = null;
            user.LastLoginUtc = now;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(user, tokens));
        }).AllowAnonymous();

        // Política "Authenticated": también la pueden usar quienes deben cambiar su contraseña
        app.MapPost("/api/auth/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal,
            AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) =>
        {
            if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
                return Results.BadRequest("La nueva contraseña debe tener al menos 8 caracteres.");
            if (req.NewPassword == req.CurrentPassword)
                return Results.BadRequest("La nueva contraseña debe ser distinta de la actual.");

            var user = await db.Users.FindAsync(principal.GetUserId());
            if (user is null || !user.Active) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Results.BadRequest("La contraseña actual no es correcta.");

            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            user.MustChangePassword = false;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(user, tokens)); // token nuevo, ya sin la marca "mcp"
        }).RequireAuthorization("Authenticated");
    }
}
