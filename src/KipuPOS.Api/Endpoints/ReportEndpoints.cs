using System.Globalization;
using KipuPOS.Api.Data;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

public static class ReportEndpoints
{
    // from inclusivo, to exclusivo, ambos con zona horaria (UTC u offset). Solo ventas válidas, salvo el conteo de anuladas.
    // branchId es opcional: sin él, el reporte suma todas las sucursales del negocio.
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports").RequireAuthorization("Admin");

        g.MapGet("/summary", async (DateTimeOffset from, DateTimeOffset to, Guid? branchId, string? groupBy,
            AppDbContext db, TenantSettings settings, CancellationToken ct) =>
        {
            var f = from.UtcDateTime; var t = to.UtcDateTime;
            if (t <= f || (t - f).TotalDays > 366) return Results.BadRequest("Rango de fechas inválido (máximo 366 días).");

            var rows = await db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Valid && s.CreatedAtUtc >= f && s.CreatedAtUtc < t
                            && (branchId == null || s.BranchId == branchId))
                .Select(s => new { s.CreatedAtUtc, s.Total }).ToListAsync(ct);
            var voidedCount = await db.Sales.CountAsync(s => s.Status == SaleStatus.Voided && s.CreatedAtUtc >= f && s.CreatedAtUtc < t
                                                             && (branchId == null || s.BranchId == branchId), ct);
            var voidedTotal = await db.Sales.Where(s => s.Status == SaleStatus.Voided && s.CreatedAtUtc >= f && s.CreatedAtUtc < t
                                                        && (branchId == null || s.BranchId == branchId))
                .SumAsync(s => s.Total, ct);

            var cfg = await settings.GetAsync(ct);
            var spanHours = (t - f).TotalHours;
            var byHour = string.Equals(groupBy ?? (spanHours <= 48 ? "hour" : "day"), "hour", StringComparison.OrdinalIgnoreCase);
            var withDate = spanHours > 24;
            var offset = TimeSpan.FromHours(cfg.UtcOffsetHours);
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

        g.MapGet("/top-products", async (DateTimeOffset from, DateTimeOffset to, Guid? branchId, int? take,
            AppDbContext db, CancellationToken ct) =>
        {
            var f = from.UtcDateTime; var t = to.UtcDateTime;
            if (t <= f || (t - f).TotalDays > 366) return Results.BadRequest("Rango de fechas inválido (máximo 366 días).");
            var n = Math.Clamp(take ?? 5, 1, 50);

            var rows = await db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Valid && s.CreatedAtUtc >= f && s.CreatedAtUtc < t
                            && (branchId == null || s.BranchId == branchId))
                .SelectMany(s => s.Lines)
                .GroupBy(l => l.ProductId)
                .Select(x => new { Name = x.Max(l => l.ProductName), Units = x.Sum(l => l.Quantity), Revenue = x.Sum(l => l.LineSubtotal) })
                .OrderByDescending(x => x.Units).ThenByDescending(x => x.Revenue)
                .Take(n).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new TopProductDto(r.Name ?? "", r.Units, r.Revenue)).ToList());
        });

        g.MapGet("/management", async (DateTimeOffset from, DateTimeOffset to, Guid? branchId,
            AppDbContext db, TenantSettings settings, CancellationToken ct) =>
        {
            var f = from.UtcDateTime; var t = to.UtcDateTime;
            if (t <= f || (t - f).TotalDays > 366) return Results.BadRequest("Rango de fechas inválido (máximo 366 días).");
            var cfg = await settings.GetAsync(ct);
            return Results.Ok(await BuildManagementAsync(db, f, t, branchId, TimeSpan.FromHours(cfg.UtcOffsetHours), ct));
        });
    }

    private static readonly string[] Weekdays = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];

    // Se calcula en memoria sobre las ventas del período: suficiente para negocios pequeños y medianos.
    // Si el volumen crece, mover las agregaciones a SQL (vistas o procedimientos) sin cambiar el contrato.
    private static async Task<ManagementReportDto> BuildManagementAsync(AppDbContext db, DateTime f, DateTime t, Guid? branchId,
        TimeSpan offset, CancellationToken ct)
    {
        var inRange = db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= f && s.CreatedAtUtc < t && (branchId == null || s.BranchId == branchId));
        var sales = await inRange.Where(s => s.Status == SaleStatus.Valid)
            .Select(s => new
            {
                s.CreatedAtUtc, s.Total, s.Subtotal, s.Igv, s.BranchId, Cashier = s.Cashier!.FullName, s.TableName,
                Payments = s.Payments.Select(p => new { p.Method, p.Amount }).ToList(),
                Lines = s.Lines.Select(l => new { l.ProductId, l.ProductName, l.Quantity, l.LineSubtotal }).ToList()
            })
            .AsSplitQuery().ToListAsync(ct);
        var voided = await inRange.Where(s => s.Status == SaleStatus.Voided).Select(s => s.Total).ToListAsync(ct);

        // Período anterior de igual duración, inmediatamente antes
        var span = t - f;
        var pf = f - span;
        var prev = db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= pf && s.CreatedAtUtc < f && (branchId == null || s.BranchId == branchId));
        var prevValid = await prev.Where(s => s.Status == SaleStatus.Valid).Select(s => new { s.Total, s.Subtotal, s.Igv }).ToListAsync(ct);
        var prevVoided = await prev.Where(s => s.Status == SaleStatus.Voided).Select(s => s.Total).ToListAsync(ct);

        static KpiDto Kpi(decimal total, decimal net, decimal igv, int tickets, List<decimal> voidedTotals) =>
            new(total, net, igv, tickets, tickets > 0 ? Money.Round(total / tickets) : 0m, voidedTotals.Count, voidedTotals.Sum());
        var current = Kpi(sales.Sum(s => s.Total), sales.Sum(s => s.Subtotal), sales.Sum(s => s.Igv), sales.Count, voided);
        var previous = Kpi(prevValid.Sum(s => s.Total), prevValid.Sum(s => s.Subtotal), prevValid.Sum(s => s.Igv), prevValid.Count, prevVoided);

        DateTime Local(DateTime utc) => utc + offset;
        var inv = CultureInfo.InvariantCulture;

        // Tendencia: por hora (≤ 2 días), por día (≤ 3 meses) o por mes
        var unit = span.TotalHours <= 48 ? "hour" : span.TotalDays <= 92 ? "day" : "month";
        List<LabeledAmountDto> trend;
        if (unit == "hour")
        {
            var withDate = span.TotalHours > 24;
            trend = sales.GroupBy(s => { var l = Local(s.CreatedAtUtc); return new DateTime(l.Year, l.Month, l.Day, l.Hour, 0, 0); })
                .OrderBy(g => g.Key)
                .Select(g => new LabeledAmountDto(g.Key.ToString(withDate ? "dd/MM HH:00" : "HH:00", inv), g.Sum(s => s.Total), g.Count())).ToList();
        }
        else
        {
            // Días o meses consecutivos, incluidos los que no tuvieron ventas (en 0), para que el gráfico no engañe
            var start = Local(f).Date; var end = Local(t).AddTicks(-1).Date;
            var keys = new List<DateTime>();
            for (var d = unit == "day" ? start : new DateTime(start.Year, start.Month, 1); d <= end; d = unit == "day" ? d.AddDays(1) : d.AddMonths(1)) keys.Add(d);
            var groups = sales.GroupBy(s => { var l = Local(s.CreatedAtUtc).Date; return unit == "day" ? l : new DateTime(l.Year, l.Month, 1); })
                .ToDictionary(g => g.Key, g => (Total: g.Sum(s => s.Total), Count: g.Count()));
            trend = keys.Select(k => groups.TryGetValue(k, out var v)
                ? new LabeledAmountDto(k.ToString(unit == "day" ? "dd/MM" : "MM/yyyy", inv), v.Total, v.Count)
                : new LabeledAmountDto(k.ToString(unit == "day" ? "dd/MM" : "MM/yyyy", inv), 0, 0)).ToList();
        }

        // Horas pico: de la primera a la última hora con ventas, sin huecos
        var byHourMap = sales.GroupBy(s => Local(s.CreatedAtUtc).Hour).ToDictionary(g => g.Key, g => (Total: g.Sum(s => s.Total), Count: g.Count()));
        var byHour = byHourMap.Count == 0 ? [] : Enumerable.Range(byHourMap.Keys.Min(), byHourMap.Keys.Max() - byHourMap.Keys.Min() + 1)
            .Select(h => byHourMap.TryGetValue(h, out var v) ? new LabeledAmountDto($"{h:00}h", v.Total, v.Count) : new LabeledAmountDto($"{h:00}h", 0, 0)).ToList();

        var byWeekdayMap = sales.GroupBy(s => ((int)Local(s.CreatedAtUtc).DayOfWeek + 6) % 7) // lunes = 0
            .ToDictionary(g => g.Key, g => (Total: g.Sum(s => s.Total), Count: g.Count()));
        var byWeekday = Enumerable.Range(0, 7)
            .Select(d => byWeekdayMap.TryGetValue(d, out var v) ? new LabeledAmountDto(Weekdays[d], v.Total, v.Count) : new LabeledAmountDto(Weekdays[d], 0, 0)).ToList();

        // Formas de pago: con pago mixto cada parte suma a su método
        var byPayment = sales.SelectMany(s => s.Payments).GroupBy(p => p.Method)
            .Select(g => new LabeledAmountDto(g.Key.ToString(), g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(x => x.Total).ToList();

        var byChannel = sales.GroupBy(s => s.TableName is null ? "Mostrador" : "Mesas")
            .Select(g => new LabeledAmountDto(g.Key, g.Sum(s => s.Total), g.Count())).OrderByDescending(x => x.Total).ToList();

        var productIds = sales.SelectMany(s => s.Lines).Select(l => l.ProductId).Distinct().ToList();
        var categoryOf = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, Category = p.Category!.Name }).ToDictionaryAsync(p => p.Id, p => p.Category, ct);
        var lines = sales.SelectMany(s => s.Lines).ToList();
        var byCategory = lines.GroupBy(l => categoryOf.TryGetValue(l.ProductId, out var c) ? c : "(sin categoría)")
            .Select(g => new LabeledAmountDto(g.Key, g.Sum(l => l.LineSubtotal), g.Sum(l => l.Quantity)))
            .OrderByDescending(x => x.Total).ToList();

        var top = lines.GroupBy(l => l.ProductId)
            .Select(g => new TopProductDto(g.Max(l => l.ProductName) ?? "", g.Sum(l => l.Quantity), g.Sum(l => l.LineSubtotal)))
            .OrderByDescending(x => x.Revenue).ThenByDescending(x => x.Units).Take(10).ToList();

        var branchNames = await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var byBranch = sales.GroupBy(s => s.BranchId)
            .Select(g => new LabeledAmountDto(branchNames.GetValueOrDefault(g.Key, "(sucursal)"), g.Sum(s => s.Total), g.Count()))
            .OrderByDescending(x => x.Total).ToList();
        var byCashier = sales.GroupBy(s => s.Cashier)
            .Select(g => new LabeledAmountDto(g.Key, g.Sum(s => s.Total), g.Count())).OrderByDescending(x => x.Total).ToList();

        return new ManagementReportDto(f, t, pf, current, previous, unit, trend, byHour, byWeekday,
            byPayment, byChannel, byCategory, byBranch, byCashier, top);
    }
}
