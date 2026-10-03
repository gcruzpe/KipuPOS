using System.Security.Claims;
using CafePos.Api.Services;
using CafePos.Api.Support;
using CafePos.Shared;

namespace CafePos.Api.Endpoints;

public static class SaleEndpoints
{
    public static void MapSales(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sales").RequireAuthorization();

        g.MapPost("", async (CreateSaleRequest r, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.CreateAsync(r, u.GetUserId(), ct)).ToHttp());

        // Admin: historial con filtros. Cajero: solo las ventas de su turno abierto (los filtros de cajero se ignoran).
        g.MapGet("", async (DateTimeOffset? from, DateTimeOffset? to, Guid? cashierId, PaymentMethod? method, SaleStatus? status,
            ClaimsPrincipal u, SaleService svc, CancellationToken ct, int page = 1, int pageSize = 25) =>
            Results.Ok(await svc.ListAsync(from?.UtcDateTime, to?.UtcDateTime, cashierId, method, status, page, pageSize,
                u.GetUserId(), u.IsAdmin(), ct)));

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.GetAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/{id:guid}/void", async (Guid id, VoidSaleRequest r, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.VoidAsync(id, r.Reason, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapPost("/{id:guid}/reprint", async (Guid id, ClaimsPrincipal u, SaleService svc, CancellationToken ct) =>
            (await svc.ReprintAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());
    }
}
