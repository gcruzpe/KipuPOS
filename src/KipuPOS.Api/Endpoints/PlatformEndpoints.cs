using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

// Alta y administración de negocios (clientes). Solo para usuarios con rol Platform,
// que inician sesión sin código de negocio y no pertenecen a ninguno.
public static partial class PlatformEndpoints
{
    [GeneratedRegex("^[a-z0-9-]{3,30}$")] private static partial Regex CodeRegex();
    [GeneratedRegex("^[a-z0-9._-]{3,30}$")] private static partial Regex UsernameRegex();
    [GeneratedRegex("^[0-9]{11}$")] private static partial Regex RucRegex();

    public static void MapPlatform(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/platform").RequireAuthorization("Platform");

        g.MapGet("/tenants", async (AppDbContext db) =>
            await db.Tenants.AsNoTracking().OrderBy(t => t.Name)
                .Select(t => new TenantDto(t.Id, t.Code, t.Name, t.Ruc, t.Active, t.IgvRate, t.UtcOffsetHours, t.CreatedAtUtc)).ToListAsync());

        // Crea el negocio, su primera sucursal (con su contador de tickets) y su administrador
        g.MapPost("/tenants", async (CreateTenantRequest r, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var code = (r.Code ?? "").Trim().ToLowerInvariant();
            var name = Text.Trunc(r.Name, 120);
            var adminUser = (r.AdminUsername ?? "").Trim().ToLowerInvariant();
            var adminName = Text.Trunc(r.AdminFullName, 120);
            var ruc = string.IsNullOrWhiteSpace(r.Ruc) ? null : r.Ruc.Trim();

            if (!CodeRegex().IsMatch(code)) return Results.BadRequest("Código inválido: 3 a 30 caracteres (minúsculas, números y guion).");
            if (name is null || adminName is null) return Results.BadRequest("El nombre del negocio y del administrador son obligatorios.");
            if (!UsernameRegex().IsMatch(adminUser)) return Results.BadRequest("Usuario del administrador inválido.");
            if (ruc is not null && !RucRegex().IsMatch(ruc)) return Results.BadRequest("El RUC debe tener 11 dígitos.");
            if (await db.Tenants.AnyAsync(t => t.Code == code)) return Results.Conflict("Ese código de negocio ya existe.");
            if (ruc is not null && await db.Tenants.AnyAsync(t => t.Ruc == ruc)) return Results.Conflict("Ese RUC ya está registrado.");

            var tenant = new Tenant { Code = code, Name = name, Ruc = ruc };
            var branch = new Branch { TenantId = tenant.Id, Name = Text.Trunc(r.BranchName, 80) ?? "Principal" };
            var counter = new DocumentCounter { TenantId = tenant.Id, BranchId = branch.Id, Type = DocumentTypes.Ticket };
            var admin = new User
            {
                TenantId = tenant.Id, BranchId = branch.Id, Username = adminUser, FullName = adminName,
                Role = Roles.Admin, MustChangePassword = true
            };
            var temp = RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyz23456789", 10);
            admin.PasswordHash = hasher.HashPassword(admin, temp);

            db.AddRange(tenant, branch, counter, admin);
            await db.SaveChangesAsync();
            return Results.Created($"/api/platform/tenants/{tenant.Id}",
                new CreateTenantResponse(tenant.ToDto(), branch.ToDto(), admin.ToDto(), temp));
        });

        // Editar datos del negocio o suspenderlo/reactivarlo (suspendido: nadie puede iniciar sesión)
        g.MapPut("/tenants/{id:guid}", async (Guid id, UpdateTenantRequest r, AppDbContext db) =>
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant is null) return Results.NotFound();
            var name = Text.Trunc(r.Name, 120);
            var ruc = string.IsNullOrWhiteSpace(r.Ruc) ? null : r.Ruc.Trim();
            if (name is null) return Results.BadRequest("El nombre es obligatorio.");
            if (ruc is not null && !RucRegex().IsMatch(ruc)) return Results.BadRequest("El RUC debe tener 11 dígitos.");
            if (ruc is not null && await db.Tenants.AnyAsync(t => t.Id != id && t.Ruc == ruc)) return Results.Conflict("Ese RUC ya está registrado.");

            tenant.Name = name; tenant.Ruc = ruc; tenant.Active = r.Active;
            await db.SaveChangesAsync();
            return Results.Ok(tenant.ToDto());
        });
    }
}
