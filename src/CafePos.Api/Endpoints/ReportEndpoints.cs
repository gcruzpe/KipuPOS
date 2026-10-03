using System.Globalization;
using CafePos.Api.Data;
using CafePos.Api.Support;
using CafePos.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CafePos.Api.Endpoints;

public static class ReportEndpoints
{
    // from inclusivo, to exclusivo, ambos con zona horaria (UTC u offset). Solo ventas válidas, salvo el conteo de anuladas.
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports").RequireAuthorization("Admin");

        g.MapGet("/summary", async (DateTimeOffset from, DateTimeOffset to, string? groupBy, AppDbContext db,
            IOptions<BusinessOptions> biz, CancellationToken ct) =>
        {
            var f = from.UtcDateTime; var t = to.UtcDateTime;
            if (t <= f || (t - f).TotalDays > 366) return Results.BadRequest("Rango de fechas inválido (máximo 366 días).");

            var rows = await db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Valid && s.CreatedAtUtc >= f && s.CreatedAtUtc < t)
                .Select(s => new { s.CreatedAtUtc, s.Total }).ToListAsync(ct);
            var voidedCount = await db.Sales.CountAsync(s => s.Status == SaleStatus.Voided && s.CreatedAtUtc >= f && s.CreatedAtUtc < t, ct);
            var voidedTotal = await db.Sales.Where(s => s.Status == SaleStatus.Voided && s.CreatedAtUtc >= f && s.CreatedAtUtc < t)
                .SumAsync(s => s.Total, ct);

            var spanHours = (t - f).TotalHours;
            var byHour = string.Equals(groupBy ?? (spanHours <= 48 ? "hour" : "day"), "hour", StringComparison.OrdinalIgnoreCase);
            var withDate = spanHours > 24;
            var offset = TimeSpan.FromHours(biz.Value.UtcOffsetHours);
            var inv = CultureInfo.InvariantCulture;

            var buckets = rows
                .GroupBy(r => { var l = r.CreatedAtUtc + offset; return byHour ? new DateTime(l.Year, l.Month, l.Day, l.Hour, 0, 0) : l.Date; })
                .OrderBy(x => x.Key)
                .Select(x => new SalesBucketDto(
                    byHour ? x.Key.ToString(withDate ? "dd/MM HH:00" : "HH:00", inv) : x.Key.ToString("dd/MM", inv),
                    x.Sum(r => r.Total), x.Count()))
                .ToList();

            var total = rows.Sum(r => r.Total);
            var avg = rows.Count > 0 ? Money.Round(total / rows.Count) : 0m;
            return Results.Ok(new SalesSummaryDto(total, rows.Count, avg, voidedCount, voidedTotal, buckets));
        });

        g.MapGet("/top-products", async (DateTimeOffset from, DateTimeOffset to, int? take, AppDbContext db, CancellationToken ct) =>
        {
            var f = from.UtcDateTime; var t = to.UtcDateTime;
            if (t <= f || (t - f).TotalDays > 366) return Results.BadRequest("Rango de fechas inválido (máximo 366 días).");
            var n = Math.Clamp(take ?? 5, 1, 50);

            var rows = await db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Valid && s.CreatedAtUtc >= f && s.CreatedAtUtc < t)
                .SelectMany(s => s.Lines)
                .GroupBy(l => l.ProductId)
                .Select(x => new { Name = x.Max(l => l.ProductName), Units = x.Sum(l => l.Quantity), Revenue = x.Sum(l => l.LineSubtotal) })
                .OrderByDescending(x => x.Units).ThenByDescending(x => x.Revenue)
                .Take(n).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new TopProductDto(r.Name, r.Units, r.Revenue)).ToList());
        });
    }
}
