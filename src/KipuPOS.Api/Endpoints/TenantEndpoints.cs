using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

// Datos del propio negocio y sus sucursales
public static class TenantEndpoints
{
    public static void MapTenantAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();

        g.MapGet("/tenant", async (ITenantContext tc, AppDbContext db) =>
        {
            var id = tc.TenantId;
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return t is null ? Results.NotFound() : Results.Ok(t.ToDto());
        });

        g.MapGet("/branches", async (AppDbContext db) =>
            await db.Branches.AsNoTracking().OrderBy(b => b.Name)
                .Select(b => new BranchDto(b.Id, b.Name, b.Address, b.Active)).ToListAsync());

        var admin = app.MapGroup("/api/branches").RequireAuthorization("Admin");

        admin.MapPost("", async (BranchUpsert r, ITenantContext tc, AppDbContext db) =>
        {
            var name = Text.Trunc(r.Name, 80);
            if (name is null) return Results.BadRequest("El nombre es obligatorio.");
            var tenantId = tc.TenantId ?? throw new InvalidOperationException("No hay negocio en contexto.");

            var branch = new Branch { TenantId = tenantId, Name = name, Address = Text.Trunc(r.Address, 200), Active = r.Active };
            db.Branches.Add(branch);
            // El contador de tickets de la sucursal nace junto con ella
            db.DocumentCounters.Add(new DocumentCounter { TenantId = tenantId, BranchId = branch.Id, Type = DocumentTypes.Ticket });
            await db.SaveChangesAsync();
            return Results.Created($"/api/branches/{branch.Id}", branch.ToDto());
        });

        admin.MapPut("/{id:guid}", async (Guid id, BranchUpsert r, AppDbContext db) =>
        {
            var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id);
            if (branch is null) return Results.NotFound();
            var name = Text.Trunc(r.Name, 80);
            if (name is null) return Results.BadRequest("El nombre es obligatorio.");
            if (!r.Active && branch.Active && !await db.Branches.AnyAsync(b => b.Id != id && b.Active))
                return Results.BadRequest("Debe quedar al menos una sucursal activa.");

            branch.Name = name; branch.Address = Text.Trunc(r.Address, 200); branch.Active = r.Active;
            await db.SaveChangesAsync();
            return Results.Ok(branch.ToDto());
        });
    }
}
