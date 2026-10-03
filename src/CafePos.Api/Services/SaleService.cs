using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Support;
using CafePos.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CafePos.Api.Services;

public class SaleService(AppDbContext db, IOptions<TaxOptions> tax)
{
    private static ServiceResult<SaleDto> Fail(int s, string m) => ServiceResult<SaleDto>.Fail(s, m);

    public async Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleRequest req, Guid cashierId, CancellationToken ct)
    {
        if (req.Id == Guid.Empty) return Fail(400, "Falta el identificador de la venta.");

        // Idempotencia: si ya existe, devolver la misma venta (reenvío tras corte de internet)
        var prior = await db.Sales.AsNoTracking().Where(s => s.Id == req.Id).Select(s => (Guid?)s.CashierId).FirstOrDefaultAsync(ct);
        if (prior is not null)
            return prior == cashierId
                ? ServiceResult<SaleDto>.Success(await LoadDtoAsync(req.Id, ct))
                : Fail(409, "El identificador de venta ya existe.");

        if (req.Lines is null || req.Lines.Count == 0) return Fail(400, "La venta no tiene productos.");
        if (req.Lines.Any(l => l.Quantity < 1 || l.Quantity > 99)) return Fail(400, "Cantidad inválida (1 a 99).");

        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.CashSessionId, ct);
        if (session is null || session.CashierId != cashierId) return Fail(404, "Turno no encontrado.");
        if (session.ClosedAtUtc is not null) return Fail(409, "El turno ya está cerrado.");

        var ids = req.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (ids.Any(id => !products.TryGetValue(id, out var found) || !found.Active))
            return Fail(400, "Hay productos inexistentes o inactivos.");

        var position = 0;
        var lines = req.Lines.Select(l =>
        {
            var p = products[l.ProductId]; // el precio SIEMPRE sale del servidor
            return new SaleLine
            {
                Position = position++, ProductId = p.Id, ProductName = p.Name, UnitPrice = p.Price,
                Quantity = l.Quantity, Note = Text.Trunc(l.Note, 200), LineSubtotal = Money.Round(p.Price * l.Quantity)
            };
        }).ToList();

        var subtotal = lines.Sum(l => l.LineSubtotal);
        var igv = Money.Round(subtotal * tax.Value.IgvRate); // IGV sobre el subtotal de la venta
        var now = DateTime.UtcNow;
        var created = req.CreatedAtUtc.ToUniversalTime();
        if (created > now.AddMinutes(5)) created = now; // reloj del cliente adelantado

        db.Sales.Add(new Sale
        {
            Id = req.Id, CreatedAtUtc = created, ReceivedAtUtc = now, CashSessionId = session.Id, CashierId = cashierId,
            Subtotal = subtotal, Igv = igv, Total = subtotal + igv, PaymentMethod = req.PaymentMethod,
            Status = SaleStatus.Valid, Lines = lines
        });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Dos reenvíos simultáneos de la misma venta
            db.ChangeTracker.Clear();
            if (await db.Sales.AsNoTracking().AnyAsync(s => s.Id == req.Id && s.CashierId == cashierId, ct))
                return ServiceResult<SaleDto>.Success(await LoadDtoAsync(req.Id, ct));
            throw;
        }
        return ServiceResult<SaleDto>.Success(await LoadDtoAsync(req.Id, ct), 201);
    }

    public async Task<ServiceResult<SaleDto>> GetAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var denied = await CheckAccessAsync(id, userId, isAdmin, ct);
        return denied ?? ServiceResult<SaleDto>.Success(await LoadDtoAsync(id, ct));
    }

    public async Task<ServiceResult<SaleDto>> VoidAsync(Guid id, string? reason, Guid userId, bool isAdmin, CancellationToken ct)
    {
        reason = Text.Trunc(reason, 200);
        if (reason is null || reason.Length < 3) return Fail(400, "El motivo de la anulación es obligatorio.");

        var denied = await CheckAccessAsync(id, userId, isAdmin, ct);
        if (denied is not null) return denied;

        var sale = await db.Sales.FirstAsync(s => s.Id == id, ct);
        if (sale.Status == SaleStatus.Voided) return Fail(409, "La venta ya está anulada.");

        var now = DateTime.UtcNow;
        sale.Status = SaleStatus.Voided;
        sale.VoidReason = reason;
        sale.VoidedById = userId;
        sale.VoidedAtUtc = now;
        db.SaleAudits.Add(new SaleAudit { SaleId = id, Type = SaleAuditType.Void, UserId = userId, AtUtc = now, Detail = reason });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Fail(409, "La venta fue modificada por otro usuario. Actualiza e intenta de nuevo."); }
        return ServiceResult<SaleDto>.Success(await LoadDtoAsync(id, ct));
    }

    public async Task<ServiceResult<SaleDto>> ReprintAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var denied = await CheckAccessAsync(id, userId, isAdmin, ct);
        if (denied is not null) return denied;

        var status = await db.Sales.AsNoTracking().Where(s => s.Id == id).Select(s => s.Status).FirstAsync(ct);
        if (status == SaleStatus.Voided) return Fail(409, "No se reimprime una venta anulada.");

        db.SaleAudits.Add(new SaleAudit { SaleId = id, Type = SaleAuditType.Reprint, UserId = userId, AtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SaleDto>.Success(await LoadDtoAsync(id, ct)); // el cliente imprime con la marca COPIA
    }

    public async Task<PagedResult<SaleListItemDto>> ListAsync(DateTime? from, DateTime? to, Guid? cashierId,
        PaymentMethod? method, SaleStatus? status, int page, int pageSize, Guid userId, bool isAdmin, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Sales.AsNoTracking().AsQueryable();
        if (!isAdmin) q = q.Where(s => s.CashierId == userId && s.CashSession!.ClosedAtUtc == null); // el cajero ve solo su turno abierto
        else if (cashierId is { } c) q = q.Where(s => s.CashierId == c);
        if (from is { } f) q = q.Where(s => s.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(s => s.CreatedAtUtc < t);
        if (method is { } m) q = q.Where(s => s.PaymentMethod == m);
        if (status is { } st) q = q.Where(s => s.Status == st);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SaleListItemDto(s.Id, s.Number, s.CreatedAtUtc, s.Cashier!.FullName, s.PaymentMethod, s.Status, s.Total))
            .ToListAsync(ct);
        return new PagedResult<SaleListItemDto>(items, total, page, pageSize);
    }

    // Admin: cualquier venta. Cajero: solo las suyas, mientras su turno siga abierto.
    private async Task<ServiceResult<SaleDto>?> CheckAccessAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var info = await db.Sales.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.CashierId, Open = s.CashSession!.ClosedAtUtc == null }).FirstOrDefaultAsync(ct);
        if (info is null) return Fail(404, "Venta no encontrada.");
        if (!isAdmin && (info.CashierId != userId || !info.Open))
            return Fail(403, "Solo puedes operar sobre ventas de tu turno abierto.");
        return null;
    }

    private async Task<SaleDto> LoadDtoAsync(Guid id, CancellationToken ct)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Cashier).Include(s => s.VoidedBy)
            .FirstAsync(s => s.Id == id, ct);
        var reprints = await db.SaleAudits.CountAsync(a => a.SaleId == id && a.Type == SaleAuditType.Reprint, ct);
        return sale.ToDto(reprints);
    }
}
