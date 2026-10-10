using System.Security.Claims;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;

namespace KipuPOS.Api.Endpoints;

public static class SaleEndpoints
{
    public static void MapSales(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sales").RequireAuthorization();

        g.MapPost("", async (CreateSaleRequest r, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.CreateAsync(r, u.GetUserId(), ct)).ToHttp());

        // Admin: historial con filtros (cajero, sucursal, método, estado). Cajero: solo las ventas de su turno abierto.
        g.MapGet("", async (DateTimeOffset? from, DateTimeOffset? to, Guid? cashierId, Guid? branchId, PaymentMethod? method, SaleStatus? status,
            ClaimsPrincipal u, SaleService svc, CancellationToken ct, int page = 1, int pageSize = 25) =>
            Results.Ok(await svc.ListAsync(from?.UtcDateTime, to?.UtcDateTime, cashierId, branchId, method, status, page, pageSize,
                u.GetUserId(), u.IsAdmin(), ct)));

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.GetAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/{id:guid}/void", async (Guid id, VoidSaleRequest r, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.VoidAsync(id, r.Reason, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/{id:guid}/reprint", async (Guid id, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.ReprintAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());
    }
}
