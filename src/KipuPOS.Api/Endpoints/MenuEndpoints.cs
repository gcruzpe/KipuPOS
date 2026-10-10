using System.Security.Claims;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;

namespace KipuPOS.Api.Endpoints;

public static class MenuEndpoints
{
    public static void MapMenu(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/menu").RequireAuthorization();

        // Menú de hoy de la sucursal del usuario (el administrador puede pedir otra con ?branchId=)
        g.MapGet("/today", async (Guid? branchId, ClaimsPrincipal u, MenuService svc, CancellationToken ct) =>
            (await svc.TodayAsync(branchId, u.IsAdmin(), ct)).ToHttp());

        g.MapPatch("/entries/{id:guid}/sold-out", async (Guid id, SoldOutRequest r, ClaimsPrincipal u, MenuService svc, CancellationToken ct) =>
            (await svc.SetSoldOutAsync(id, r.SoldOut, u.IsAdmin(), ct)).ToHttp());

        var admin = app.MapGroup("/api/menu").RequireAuthorization("Admin");

        admin.MapGet("/{date}", async (DateOnly date, Guid? branchId, MenuService svc, CancellationToken ct) =>
            (await svc.GetAsync(date, branchId, ct)).ToHttp());

        admin.MapPut("/{date}", async (DateOnly date, Guid? branchId, SetMenuRequest r, MenuService svc, CancellationToken ct) =>
            (await svc.SetAsync(date, branchId, r.Entries, ct)).ToHttp());

        admin.MapPost("/{date}/copy-from/{sourceDate}", async (DateOnly date, DateOnly sourceDate, Guid? branchId, MenuService svc, CancellationToken ct) =>
            (await svc.CopyAsync(date, sourceDate, branchId, ct)).ToHttp());
    }
}
