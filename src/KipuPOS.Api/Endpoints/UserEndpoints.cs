using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

public static partial class UserEndpoints
{
    [GeneratedRegex("^[a-z0-9._-]{3,30}$")]
    private static partial Regex UsernameRegex();

    private static string NewTempPassword() => RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyz23456789", 10);

    // Un cajero necesita sucursal; la sucursal, si se indica, debe existir, estar activa y ser de este negocio
    private static async Task<string?> ValidateBranchAsync(AppDbContext db, string role, Guid? branchId)
    {
        if (role == Roles.Cajero && branchId is null) return "Un cajero debe tener una sucursal asignada.";
        if (branchId is { } id && !await db.Branches.AnyAsync(b => b.Id == id && b.Active)) return "Sucursal inválida.";
        return null;
    }

    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").RequireAuthorization("Admin");

        g.MapGet("", async (AppDbContext db) =>
            await db.Users.AsNoTracking().OrderBy(u => u.FullName)
                .Select(u => new UserDto(u.Id, u.FullName, u.Username, u.Role, u.Active, u.LastLoginUtc, u.BranchId)).ToListAsync());

        g.MapPost("", async (CreateUserRequest r, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var username = (r.Username ?? "").Trim().ToLowerInvariant();
            var fullName = Text.Trunc(r.FullName, 120);
            if (fullName is null) return Results.BadRequest("El nombre es obligatorio.");
            if (!UsernameRegex().IsMatch(username)) return Results.BadRequest("Usuario inválido: 3 a 30 caracteres (letras, números, punto, guion).");
            if (!Roles.TenantRoles.Contains(r.Role)) return Results.BadRequest("Rol inválido.");
            if (await ValidateBranchAsync(db, r.Role, r.BranchId) is { } branchError) return Results.BadRequest(branchError);
            if (await db.Users.AnyAsync(u => u.Username == username)) return Results.Conflict("Ese usuario ya existe.");

            var user = new User { Username = username, FullName = fullName, Role = r.Role, BranchId = r.BranchId, MustChangePassword = true };
            var temp = NewTempPassword();
            user.PasswordHash = hasher.HashPassword(user, temp);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"/api/users/{user.Id}", new TempPasswordResponse(user.ToDto(), temp));
        });

        g.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest r, ClaimsPrincipal me, AppDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var fullName = Text.Trunc(r.FullName, 120);
            if (fullName is null || !Roles.TenantRoles.Contains(r.Role)) return Results.BadRequest("Datos inválidos.");
            if (await ValidateBranchAsync(db, r.Role, r.BranchId) is { } branchError) return Results.BadRequest(branchError);

            var losesAdmin = user.Role == Roles.Admin && user.Active && (!r.Active || r.Role != Roles.Admin);
            if (losesAdmin)
            {
                if (user.Id == me.GetUserId())
                    return Results.BadRequest("No puedes desactivar tu cuenta ni quitarte el rol de administrador.");
                if (!await db.Users.AnyAsync(u => u.Id != id && u.Role == Roles.Admin && u.Active))
                    return Results.BadRequest("Debe quedar al menos un administrador activo.");
            }

            user.FullName = fullName; user.Role = r.Role; user.Active = r.Active; user.BranchId = r.BranchId;
            await db.SaveChangesAsync();
            return Results.Ok(user.ToDto());
        });

        g.MapPost("/{id:guid}/reset-password", async (Guid id, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var temp = NewTempPassword();
            user.PasswordHash = hasher.HashPassword(user, temp);
            user.MustChangePassword = true;
            user.FailedLoginCount = 0;
            user.LockoutUntilUtc = null;
            await db.SaveChangesAsync();
            return Results.Ok(new TempPasswordResponse(user.ToDto(), temp));
        });
    }
}
