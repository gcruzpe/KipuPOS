using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Services;

public class SaleService(AppDbContext db, TenantSettings settings, DocumentNumberService numbers, ILogger<SaleService> log)
{
    private static ServiceResult<SaleDto> Fail(int s, string m) => ServiceResult<SaleDto>.Fail(s, m);

    // tableOrderId: venta que sale de una cuenta de mesa. Los cambios que TableOrderService dejó pendientes en este mismo
    // DbContext (descontar lo cobrado de la cuenta) se guardan en la MISMA transacción que la venta.
    public async Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleRequest req, Guid cashierId, CancellationToken ct,
        Guid? tableOrderId = null, string? tableName = null)
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

        var cfg = await settings.GetAsync(ct);
        var now = DateTime.UtcNow;
        var created = req.CreatedAtUtc.ToUniversalTime();
        if (created > now.AddMinutes(5)) created = now; // reloj del cliente adelantado
        var businessDate = DateOnly.FromDateTime(created.AddHours(cfg.UtcOffsetHours));

        var ids = req.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (ids.Any(id => !products.TryGetValue(id, out var found) || !found.Active))
            return Fail(400, "Hay productos inexistentes o inactivos.");

        // Platos de menú: el precio es el del menú de la fecha de la venta
        var menuPrices = await db.DailyMenuEntries.AsNoTracking()
            .Where(e => e.BranchId == session.BranchId && e.Date == businessDate && ids.Contains(e.ProductId))
            .ToDictionaryAsync(e => e.ProductId, e => e.Price, ct);
        var missing = products.Values.FirstOrDefault(p => p.IsMenuItem && !menuPrices.ContainsKey(p.Id));
        if (missing is not null) return Fail(400, $"«{missing.Name}» no está en el menú de ese día.");

        var position = 0;
        var lines = req.Lines.Select(l =>
        {
            var p = products[l.ProductId]; // el precio SIEMPRE sale del servidor
            var unit = p.IsMenuItem ? menuPrices[p.Id] : p.Price;
            return new SaleLine
            {
                Position = position++, ProductId = p.Id, ProductName = p.Name, UnitPrice = unit,
                Quantity = l.Quantity, Note = Text.Trunc(l.Note, 200), LineSubtotal = Money.Round(unit * l.Quantity)
            };
        }).ToList();

        var subtotal = lines.Sum(l => l.LineSubtotal);
        var igv = Money.Round(subtotal * cfg.IgvRate); // IGV sobre el subtotal de la venta
        var total = subtotal + igv;

        // Pagos: uno solo por defecto; en pago mixto deben sumar exactamente el total calculado por el servidor
        List<SalePayment> payments;
        if (req.Payments is { Count: > 0 } parts)
        {
            if (parts.Count > 4) return Fail(400, "Máximo 4 formas de pago por venta.");
            if (parts.Any(p => p.Amount <= 0 || !Enum.IsDefined(p.Method))) return Fail(400, "Hay un pago inválido.");
            payments = parts.GroupBy(p => p.Method)
                .Select(g => new SalePayment { Method = g.Key, Amount = Money.Round(g.Sum(p => p.Amount)) }).ToList();
            var paid = payments.Sum(p => p.Amount);
            if (paid != total) return Fail(400, $"Los pagos suman S/ {paid:0.00} y el total es S/ {total:0.00}.");
        }
        else
        {
            payments = [new SalePayment { Method = req.PaymentMethod, Amount = total }];
        }

        var sale = new Sale
        {
            Id = req.Id, BranchId = session.BranchId, CreatedAtUtc = created, ReceivedAtUtc = now,
            CashSessionId = session.Id, CashierId = cashierId,
            Subtotal = subtotal, Igv = igv, Total = total,
            PaymentMethod = payments.OrderByDescending(p => p.Amount).First().Method, // pago principal
            Status = SaleStatus.Valid, Lines = lines, Payments = payments, TableOrderId = tableOrderId, TableName = tableName
        };

        // El número y la venta se confirman juntos: si algo falla, el número vuelve a quedar libre
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            sale.Number = await numbers.NextAsync(session.BranchId, ct);
            db.Sales.Add(sale);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // Dos reenvíos simultáneos de la misma venta: gana uno y el otro devuelve la venta ya registrada
            if (await db.Sales.AsNoTracking().AnyAsync(s => s.Id == req.Id && s.CashierId == cashierId, ct))
                return ServiceResult<SaleDto>.Success(await LoadDtoAsync(req.Id, ct));
            log.LogWarning(ex, "No se pudo registrar la venta {SaleId}", req.Id);
            return Fail(409, "No se pudo registrar la venta (identificador duplicado o conflicto). Reintenta.");
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

    public async Task<PagedResult<SaleListItemDto>> ListAsync(DateTime? from, DateTime? to, Guid? cashierId, Guid? branchId,
        PaymentMethod? method, SaleStatus? status, int page, int pageSize, Guid userId, bool isAdmin, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Sales.AsNoTracking().AsQueryable();
        if (!isAdmin)
        {
            q = q.Where(s => s.CashierId == userId && s.CashSession!.ClosedAtUtc == null); // el cajero ve solo su turno abierto
        }
        else
        {
            if (cashierId is { } c) q = q.Where(s => s.CashierId == c);
            if (branchId is { } br) q = q.Where(s => s.BranchId == br);
        }
        if (from is { } f) q = q.Where(s => s.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(s => s.CreatedAtUtc < t);
        if (method is { } m) q = q.Where(s => s.PaymentMethod == m);
        if (status is { } st) q = q.Where(s => s.Status == st);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SaleListItemDto(s.Id, s.Number, s.CreatedAtUtc, s.Cashier!.FullName, s.PaymentMethod, s.Status, s.Total,
                s.Payments.Count > 1, s.TableName))
            .ToListAsync(ct);
        return new PagedResult<SaleListItemDto>(items, total, page, pageSize);
    }

    // Admin: cualquier venta del negocio. Cajero: solo las suyas, mientras su turno siga abierto.
    private async Task<ServiceResult<SaleDto>?> CheckAccessAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var info = await db.Sales.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.CashierId, Open = s.CashSession!.ClosedAtUtc == null }).FirstOrDefaultAsync(ct);
        if (info is null) return Fail(404, "Venta no encontrada.");
        if (!isAdmin && (info.CashierId != userId || !info.Open))
            return Fail(403, "Solo puedes operar sobre ventas de tu turno abierto.");
        return null;
    }

    public async Task<SaleDto> LoadDtoAsync(Guid id, CancellationToken ct)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments).Include(s => s.Cashier)
            .Include(s => s.VoidedBy)
            .FirstAsync(s => s.Id == id, ct);
        var reprints = await db.SaleAudits.CountAsync(a => a.SaleId == id && a.Type == SaleAuditType.Reprint, ct);
        return sale.ToDto(reprints);
    }
}
