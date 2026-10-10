using System.Security.Claims;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;

namespace KipuPOS.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessions(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sessions").RequireAuthorization();

        g.MapPost("/open", async (OpenSessionRequest r, ClaimsPrincipal u, CashSessionService svc, CancellationToken ct) =>
            (await svc.OpenAsync(r.OpeningCash, u.GetUserId(), ct)).ToHttp());

        g.MapGet("/current", async (ClaimsPrincipal u, CashSessionService svc, CancellationToken ct) =>
            await svc.CurrentAsync(u.GetUserId(), ct) is { } s ? Results.Ok(s) : Results.NoContent());

        g.MapPost("/{id:guid}/close", async (Guid id, CloseSessionRequest r, ClaimsPrincipal u, CashSessionService svc, CancellationToken ct) =>
            (await svc.CloseAsync(id, r.CountedCash, r.Note, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        g.MapGet("/{id:guid}/summary", async (Guid id, ClaimsPrincipal u, CashSessionService svc, CancellationToken ct) =>
            (await svc.GetSummaryAsync(id, u.GetUserId(), u.IsAdmin(), ct)).ToHttp());

        // Admin: historial de cierres y diferencias (opcionalmente por sucursal)
        g.MapGet("", async (DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, CashSessionService svc, CancellationToken ct,
                int page = 1, int pageSize = 25) =>
            Results.Ok(await svc.ListAsync(from?.UtcDateTime, to?.UtcDateTime, branchId, page, pageSize, ct)))
            .RequireAuthorization("Admin");
    }
}
