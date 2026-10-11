using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Services;

public class CashSessionService(AppDbContext db)
{
    public async Task<ServiceResult<CashSessionDto>> OpenAsync(decimal openingCash, Guid userId, CancellationToken ct)
    {
        if (openingCash < 0 || openingCash > 100_000) return ServiceResult<CashSessionDto>.Fail(400, "Fondo inicial inválido.");

        // La sucursal del turno es la del usuario (se lee de la base, no del token, por si cambió)
        var branchId = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.BranchId).FirstOrDefaultAsync(ct);
        if (branchId is null) return ServiceResult<CashSessionDto>.Fail(400, "Tu usuario no tiene sucursal asignada.");
        if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.Active, ct))
            return ServiceResult<CashSessionDto>.Fail(400, "Tu sucursal está inactiva.");

        if (await db.CashSessions.AnyAsync(s => s.CashierId == userId && s.ClosedAtUtc == null, ct))
            return ServiceResult<CashSessionDto>.Fail(409, "Ya tienes un turno abierto.");

        var session = new CashSession
        {
            CashierId = userId, BranchId = branchId.Value, OpenedAtUtc = DateTime.UtcNow, OpeningCash = Money.Round(openingCash)
        };
        db.CashSessions.Add(session);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return ServiceResult<CashSessionDto>.Fail(409, "Ya tienes un turno abierto."); }
        return ServiceResult<CashSessionDto>.Success((await LoadAsync(session.Id, ct))!, 201);
    }

    public async Task<CashSessionDto?> CurrentAsync(Guid userId, CancellationToken ct)
    {
        var id = await db.CashSessions.AsNoTracking().Where(s => s.CashierId == userId && s.ClosedAtUtc == null)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await LoadAsync(id.Value, ct);
    }

    public async Task<ServiceResult<SessionSummaryDto>> CloseAsync(Guid id, decimal countedCash, string? note,
        Guid userId, bool isAdmin, CancellationToken ct)
    {
        var s = await db.CashSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return ServiceResult<SessionSummaryDto>.Fail(404, "Turno no encontrado.");
        if (!isAdmin && s.CashierId != userId) return ServiceResult<SessionSummaryDto>.Fail(403, "Ese turno no es tuyo.");
        if (s.ClosedAtUtc is not null) return ServiceResult<SessionSummaryDto>.Fail(409, "El turno ya está cerrado.");
        if (countedCash < 0 || countedCash > 1_000_000) return ServiceResult<SessionSummaryDto>.Fail(400, "Monto contado inválido.");

        // Con pago mixto solo cuenta la parte pagada en efectivo
        var cashSales = await ValidPayments(id).Where(p => p.Method == PaymentMethod.Efectivo).SumAsync(p => p.Amount, ct);

        s.ClosedAtUtc = DateTime.UtcNow;
        s.CountedCash = Money.Round(countedCash);
        s.ExpectedCash = s.OpeningCash + cashSales;
        s.Difference = s.CountedCash - s.ExpectedCash;
        s.Note = Text.Trunc(note, 200);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ServiceResult<SessionSummaryDto>.Fail(409, "El turno ya fue cerrado."); }
        return ServiceResult<SessionSummaryDto>.Success(await BuildSummaryAsync(id, ct));
    }

    public async Task<ServiceResult<SessionSummaryDto>> GetSummaryAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var owner = await db.CashSessions.AsNoTracking().Where(s => s.Id == id).Select(s => (Guid?)s.CashierId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<SessionSummaryDto>.Fail(404, "Turno no encontrado.");
        if (!isAdmin && owner != userId) return ServiceResult<SessionSummaryDto>.Fail(403, "Ese turno no es tuyo.");
        return ServiceResult<SessionSummaryDto>.Success(await BuildSummaryAsync(id, ct));
    }

    public async Task<PagedResult<CashSessionDto>> ListAsync(DateTime? from, DateTime? to, Guid? branchId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.CashSessions.AsNoTracking().AsQueryable();
        if (branchId is { } b) q = q.Where(s => s.BranchId == b);
        if (from is { } f) q = q.Where(s => s.OpenedAtUtc >= f);
        if (to is { } t) q = q.Where(s => s.OpenedAtUtc < t);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.OpenedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new CashSessionDto(s.Id, s.BranchId, s.CashierId, s.Cashier!.FullName, s.OpenedAtUtc, s.ClosedAtUtc,
                s.OpeningCash, s.CountedCash, s.ExpectedCash, s.Difference)).ToListAsync(ct);
        return new PagedResult<CashSessionDto>(items, total, page, pageSize);
    }

    private Task<CashSessionDto?> LoadAsync(Guid id, CancellationToken ct) =>
        db.CashSessions.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new CashSessionDto(s.Id, s.BranchId, s.CashierId, s.Cashier!.FullName, s.OpenedAtUtc, s.ClosedAtUtc,
                s.OpeningCash, s.CountedCash, s.ExpectedCash, s.Difference))
            .FirstOrDefaultAsync(ct);

    private async Task<SessionSummaryDto> BuildSummaryAsync(Guid id, CancellationToken ct)
    {
        var session = (await LoadAsync(id, ct))!;
        // Por forma de pago a partir de las partes del pago: una venta mixta aparece en cada método que usó
        var groups = await ValidPayments(id)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Count = g.Select(x => x.SaleId).Distinct().Count(), Total = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var byPayment = groups.OrderBy(g => g.Method).Select(g => new PaymentTotalDto(g.Method, g.Count, g.Total)).ToList();
        var validSales = db.Sales.AsNoTracking().Where(s => s.CashSessionId == id && s.Status == SaleStatus.Valid);
        var totalIgv = await validSales.SumAsync(s => s.Igv, ct);
        var totalSales = await validSales.SumAsync(s => s.Total, ct);
        var voidedCount = await db.Sales.AsNoTracking().CountAsync(s => s.CashSessionId == id && s.Status == SaleStatus.Voided, ct);
        var voidedTotal = await db.Sales.AsNoTracking().Where(s => s.CashSessionId == id && s.Status == SaleStatus.Voided).SumAsync(s => s.Total, ct);

        var cashSales = byPayment.Where(p => p.Method == PaymentMethod.Efectivo).Sum(p => p.Total);
        // En un turno cerrado se conserva el esperado guardado al cierre (anular después no lo altera)
        var expected = session.ExpectedCash ?? session.OpeningCash + cashSales;
        return new SessionSummaryDto(session, cashSales, expected, totalSales, totalIgv, byPayment, voidedCount, voidedTotal);
    }

    private IQueryable<SalePayment> ValidPayments(Guid sessionId) =>
        db.SalePayments.AsNoTracking().Where(p => db.Sales.Any(s => s.Id == p.SaleId && s.CashSessionId == sessionId && s.Status == SaleStatus.Valid));
}
