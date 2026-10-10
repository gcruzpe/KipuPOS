using System.Security.Claims;
using KipuPOS.Api.Auth;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

    private static LoginResponse ToResponse(User u, Tenant? t, TokenService tokens)
    {
        var (token, exp) = tokens.Create(u);
        return new LoginResponse(token, exp, u.Username, u.FullName, u.Role, u.MustChangePassword, t?.Name, u.BranchId);
    }

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) =>
        {
            var code = (req.TenantCode ?? "").Trim().ToLowerInvariant();
            var username = (req.Username ?? "").Trim().ToLowerInvariant();

            // Aún no hay token, así que no hay negocio en contexto: los usuarios se buscan sin filtro
            // y se acotan a mano por negocio (o a usuarios de plataforma si no se indica código).
            Tenant? tenant = null;
            User? user;
            if (code.Length == 0)
            {
                user = await db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.TenantId == null && u.Role == Roles.Platform && u.Username == username && u.Active);
            }
            else
            {
                tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Code == code && t.Active);
                if (tenant is null) return Results.Unauthorized();
                var tenantId = tenant.Id;
                user = await db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Username == username && u.Active);
            }
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
            return Results.Ok(ToResponse(user, tenant, tokens));
        }).AllowAnonymous();

        // Política "Authenticated": también la pueden usar quienes deben cambiar su contraseña
        app.MapPost("/api/auth/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal,
            AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) =>
        {
            if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
                return Results.BadRequest("La nueva contraseña debe tener al menos 8 caracteres.");
            if (req.NewPassword == req.CurrentPassword)
                return Results.BadRequest("La nueva contraseña debe ser distinta de la actual.");

            // El id sale del token firmado; se busca sin filtro porque los usuarios de plataforma no tienen negocio
            var userId = principal.GetUserId();
            var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && u.Active);
            if (user is null) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Results.BadRequest("La contraseña actual no es correcta.");

            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            user.MustChangePassword = false;
            await db.SaveChangesAsync();

            Tenant? tenant = null;
            if (user.TenantId is { } tid) tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tid);
            return Results.Ok(ToResponse(user, tenant, tokens)); // token nuevo, ya sin la marca "mcp"
        }).RequireAuthorization("Authenticated");
    }
}
