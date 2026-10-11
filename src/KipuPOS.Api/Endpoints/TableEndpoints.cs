using System.Security.Claims;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

// Mesas (configuración) y cuentas abiertas por mesa (operación de la caja)
public static class TableEndpoints
{
    private static DiningTableDto ToDto(this DiningTable t) => new(t.Id, t.BranchId, t.Name, t.SortOrder, t.Active);

    public static void MapTables(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        var admin = app.MapGroup("/api/tables").RequireAuthorization("Admin");

        // Admin: todas las mesas del negocio. Cajero: las activas de su sucursal.
        g.MapGet("/tables", async (ClaimsPrincipal u, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.DiningTables.AsNoTracking();
            if (!u.IsAdmin())
            {
                var userId = u.GetUserId();
                var branch = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.BranchId).FirstOrDefaultAsync(ct);
                q = q.Where(t => t.BranchId == branch && t.Active);
            }
            return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Select(t => t.ToDto()).ToListAsync(ct);
        });

        admin.MapPost("", async (DiningTableUpsert r, AppDbContext db, CancellationToken ct) =>
        {
            var name = Text.Trunc(r.Name, 40);
            if (name is null) return Results.BadRequest("El nombre de la mesa es obligatorio.");
            if (!await db.Branches.AnyAsync(b => b.Id == r.BranchId, ct)) return Results.BadRequest("Sucursal inválida.");
            if (await db.DiningTables.AnyAsync(t => t.BranchId == r.BranchId && t.Name == name, ct)) return Results.Conflict("Ya existe una mesa con ese nombre en la sucursal.");
            var table = new DiningTable { BranchId = r.BranchId, Name = name, SortOrder = r.SortOrder, Active = r.Active };
            db.DiningTables.Add(table);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tables/{table.Id}", table.ToDto());
        });

        admin.MapPut("/{id:guid}", async (Guid id, DiningTableUpsert r, AppDbContext db, CancellationToken ct) =>
        {
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (table is null) return Results.NotFound();
            var name = Text.Trunc(r.Name, 40);
            if (name is null) return Results.BadRequest("El nombre de la mesa es obligatorio.");
            if (r.BranchId != table.BranchId) return Results.BadRequest("Una mesa no puede cambiar de sucursal.");
            if (await db.DiningTables.AnyAsync(t => t.Id != id && t.BranchId == table.BranchId && t.Name == name, ct))
                return Results.Conflict("Ya existe una mesa con ese nombre en la sucursal.");
            if (!r.Active && await db.TableOrders.AnyAsync(o => o.TableId == id && o.Status == TableOrderStatus.Open, ct))
                return Results.BadRequest("La mesa tiene una cuenta abierta: cóbrala antes de desactivarla.");
            table.Name = name; table.SortOrder = r.SortOrder; table.Active = r.Active;
            await db.SaveChangesAsync(ct);
            return Results.Ok(table.ToDto());
        });

        // Operación (cajero o admin)
        g.MapGet("/tables/board", async (Guid? branchId, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.BoardAsync(u.GetUserId(), u.IsAdmin(), branchId, ct)).ToHttp());

        g.MapPost("/tables/{id:guid}/order", async (Guid id, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.OpenAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.GetAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPut("/orders/{id:guid}/lines", async (Guid id, SetOrderLinesRequest r, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.SetLinesAsync(id, r.Lines, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/orders/{id:guid}/move", async (Guid id, MoveOrderRequest r, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.MoveAsync(id, r.TableId, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/orders/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.CancelAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/orders/{id:guid}/checkout", async (Guid id, CheckoutOrderRequest r, ClaimsPrincipal u, TableOrderService svc, CancellationToken ct) =>
            (await svc.CheckoutAsync(id, r, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());
    }
}
