using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Services;

// Cuentas abiertas por mesa: se van llenando mientras el cliente consume y se cobran al final, completas o divididas.
// Los precios no se guardan en la cuenta: se muestran con los de hoy y al cobrar los fija SaleService (fuente única).
public class TableOrderService(AppDbContext db, TenantSettings settings, SaleService sales)
{
    private const int MaxLines = 60;

    private static ServiceResult<T> Fail<T>(int status, string error) => ServiceResult<T>.Fail(status, error);

    // Sucursal del usuario (leída de la base, como al abrir turno); un administrador puede consultar otra
    private async Task<(Guid? BranchId, string? Error)> ResolveBranchAsync(Guid userId, bool isAdmin, Guid? requested, CancellationToken ct)
    {
        if (isAdmin && requested is { } r)
            return await db.Branches.AnyAsync(b => b.Id == r, ct) ? (r, null) : (null, "Sucursal inválida.");
        var own = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.BranchId).FirstOrDefaultAsync(ct);
        return own is { } b ? (b, null) : (null, "Tu usuario no tiene sucursal asignada.");
    }

    // Cajero: solo mesas de su sucursal. Administrador: cualquiera del negocio.
    private async Task<string?> CheckBranchAccessAsync(Guid branchId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin) return null;
        var own = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.BranchId).FirstOrDefaultAsync(ct);
        return own == branchId ? null : "Esa mesa es de otra sucursal.";
    }

    public async Task<ServiceResult<List<TableBoardItemDto>>> BoardAsync(Guid userId, bool isAdmin, Guid? branchId, CancellationToken ct)
    {
        var (branch, error) = await ResolveBranchAsync(userId, isAdmin, branchId, ct);
        if (branch is not { } br) return Fail<List<TableBoardItemDto>>(400, error!);

        var orders = await db.TableOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.BranchId == br && o.Status == TableOrderStatus.Open).ToListAsync(ct);
        var busy = orders.Select(o => o.TableId).ToList();
        var tables = await db.DiningTables.AsNoTracking()
            .Where(t => t.BranchId == br && (t.Active || busy.Contains(t.Id)))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var prices = await PricesAsync(br, orders.SelectMany(o => o.Lines).Select(l => l.ProductId).Distinct().ToList(), ct);

        var board = tables.Select(t =>
        {
            var o = orders.FirstOrDefault(x => x.TableId == t.Id);
            var subtotal = o?.Lines.Sum(l => prices.TryGetValue(l.ProductId, out var p) ? Money.Round(p.Price * l.Quantity) : 0) ?? 0;
            return new TableBoardItemDto(t.Id, t.Name, o?.Id, o?.OpenedAtUtc, o?.Lines.Sum(l => l.Quantity) ?? 0, subtotal);
        }).ToList();
        return ServiceResult<List<TableBoardItemDto>>.Success(board);
    }

    // Abre la cuenta de la mesa; si ya hay una abierta, devuelve esa (dos cajas pueden tocar la misma mesa a la vez)
    public async Task<ServiceResult<TableOrderDto>> OpenAsync(Guid tableId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var table = await db.DiningTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tableId, ct);
        if (table is null || !table.Active) return Fail<TableOrderDto>(404, "Mesa no encontrada.");
        if (await CheckBranchAccessAsync(table.BranchId, userId, isAdmin, ct) is { } denied) return Fail<TableOrderDto>(403, denied);

        var existing = await OpenOrderIdAsync(tableId, ct);
        if (existing is { } id) return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(id, ct));

        var order = new TableOrder { BranchId = table.BranchId, TableId = tableId, OpenedById = userId, OpenedAtUtc = DateTime.UtcNow };
        db.TableOrders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await OpenOrderIdAsync(tableId, ct) is { } other) return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(other, ct));
            throw;
        }
        return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(order.Id, ct), 201);
    }

    public async Task<ServiceResult<TableOrderDto>> GetAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var order = await db.TableOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return Fail<TableOrderDto>(404, "Cuenta no encontrada.");
        if (await CheckBranchAccessAsync(order.BranchId, userId, isAdmin, ct) is { } denied) return Fail<TableOrderDto>(403, denied);
        return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(orderId, ct));
    }

    // Reemplaza los productos de la cuenta por los enviados (la caja manda siempre la lista completa)
    public async Task<ServiceResult<TableOrderDto>> SetLinesAsync(Guid orderId, IReadOnlyList<OrderLineRequest>? lines,
        Guid userId, bool isAdmin, CancellationToken ct)
    {
        var (order, error) = await LoadOpenAsync(orderId, userId, isAdmin, ct);
        if (order is null) return Fail<TableOrderDto>(error!.Value.Status, error.Value.Message);

        lines ??= [];
        if (lines.Count > MaxLines) return Fail<TableOrderDto>(400, $"Máximo {MaxLines} líneas por cuenta.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > 99)) return Fail<TableOrderDto>(400, "Cantidad inválida (1 a 99).");
        var merged = Merge(lines.Select(l => (l.ProductId, l.Quantity, Text.Trunc(l.Note, 200))));

        if (await ValidateProductsAsync(order.BranchId, merged.Select(m => m.ProductId).Distinct().ToList(), ct) is { } invalid)
            return Fail<TableOrderDto>(400, invalid);

        db.TableOrderLines.RemoveRange(order.Lines);
        var position = 0;
        foreach (var m in merged)
            db.TableOrderLines.Add(new TableOrderLine { OrderId = order.Id, Position = position++, ProductId = m.ProductId, Quantity = m.Quantity, Note = m.Note });
        await db.SaveChangesAsync(ct);
        return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(order.Id, ct));
    }

    // Mesa destino libre: la cuenta se muda. Ocupada: los productos se suman a esa cuenta y esta se cierra.
    public async Task<ServiceResult<TableOrderDto>> MoveAsync(Guid orderId, Guid targetTableId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var (order, error) = await LoadOpenAsync(orderId, userId, isAdmin, ct);
        if (order is null) return Fail<TableOrderDto>(error!.Value.Status, error.Value.Message);
        if (order.TableId == targetTableId) return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(order.Id, ct));

        var target = await db.DiningTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == targetTableId, ct);
        if (target is null || !target.Active || target.BranchId != order.BranchId)
            return Fail<TableOrderDto>(400, "La mesa destino no es válida.");

        var targetOrder = await db.TableOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.TableId == targetTableId && o.Status == TableOrderStatus.Open, ct);
        Guid resultId;
        if (targetOrder is null)
        {
            order.TableId = targetTableId;
            resultId = order.Id;
        }
        else
        {
            var combined = Merge(targetOrder.Lines.OrderBy(l => l.Position).Select(l => (l.ProductId, l.Quantity, l.Note))
                .Concat(order.Lines.OrderBy(l => l.Position).Select(l => (l.ProductId, l.Quantity, l.Note))));
            if (combined.Count > MaxLines) return Fail<TableOrderDto>(400, $"Las cuentas unidas superan {MaxLines} líneas.");
            if (combined.Any(c => c.Quantity > 99)) return Fail<TableOrderDto>(400, "Al unir, un producto superaría 99 unidades.");

            db.TableOrderLines.RemoveRange(targetOrder.Lines);
            db.TableOrderLines.RemoveRange(order.Lines);
            var position = 0;
            foreach (var c in combined)
                db.TableOrderLines.Add(new TableOrderLine { OrderId = targetOrder.Id, Position = position++, ProductId = c.ProductId, Quantity = c.Quantity, Note = c.Note });
            order.Status = TableOrderStatus.Cancelled; // se unió a otra mesa: no queda nada que cobrar aquí
            order.ClosedAtUtc = DateTime.UtcNow;
            resultId = targetOrder.Id;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Fail<TableOrderDto>(409, "La mesa cambió mientras tanto. Actualiza e inténtalo de nuevo."); }
        return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(resultId, ct));
    }

    // Liberar una mesa sin cobrar: solo si la cuenta está vacía (lo consumido se cobra o se quita a propósito)
    public async Task<ServiceResult<TableOrderDto>> CancelAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var (order, error) = await LoadOpenAsync(orderId, userId, isAdmin, ct);
        if (order is null) return Fail<TableOrderDto>(error!.Value.Status, error.Value.Message);
        if (order.Lines.Count > 0) return Fail<TableOrderDto>(409, "La cuenta tiene productos: cóbralos o quítalos antes de liberar la mesa.");
        order.Status = TableOrderStatus.Cancelled;
        order.ClosedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<TableOrderDto>.Success(await LoadDtoAsync(order.Id, ct));
    }

    public async Task<ServiceResult<CheckoutResultDto>> CheckoutAsync(Guid orderId, CheckoutOrderRequest req, Guid userId, bool isAdmin, CancellationToken ct)
    {
        // Reintento de un cobro que ya se registró (corte de red): la venta y el descuento de la cuenta se guardaron juntos
        if (req.SaleId != Guid.Empty && await db.Sales.AsNoTracking().AnyAsync(s => s.Id == req.SaleId, ct))
        {
            var again = await sales.CreateAsync(ToSaleRequest(req, []), userId, ct, orderId);
            if (!again.Ok) return Fail<CheckoutResultDto>(again.Status, again.Error!);
            var still = await db.TableOrders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.Status == TableOrderStatus.Open, ct);
            return ServiceResult<CheckoutResultDto>.Success(new CheckoutResultDto(again.Value!, still ? await LoadDtoAsync(orderId, ct) : null));
        }

        var (order, error) = await LoadOpenAsync(orderId, userId, isAdmin, ct);
        if (order is null) return Fail<CheckoutResultDto>(error!.Value.Status, error.Value.Message);
        if (order.Lines.Count == 0) return Fail<CheckoutResultDto>(400, "La cuenta está vacía.");

        var sessionBranch = await db.CashSessions.AsNoTracking().Where(s => s.Id == req.CashSessionId).Select(s => (Guid?)s.BranchId).FirstOrDefaultAsync(ct);
        if (sessionBranch is not null && sessionBranch != order.BranchId) return Fail<CheckoutResultDto>(400, "La mesa es de otra sucursal que tu turno.");

        // Qué se cobra: todo, o solo lo elegido (dividir la cuenta)
        List<(Guid ProductId, int Quantity, string? Note)> charge;
        if (req.Lines is null)
        {
            charge = order.Lines.OrderBy(l => l.Position).Select(l => (l.ProductId, l.Quantity, l.Note)).ToList();
        }
        else
        {
            charge = Merge(req.Lines.Where(l => l.Quantity > 0).Select(l => (l.ProductId, l.Quantity, Text.Trunc(l.Note, 200))));
            if (charge.Count == 0) return Fail<CheckoutResultDto>(400, "Elige al menos un producto para cobrar.");
        }

        foreach (var c in charge)
        {
            var line = order.Lines.FirstOrDefault(l => l.ProductId == c.ProductId && l.Note == c.Note);
            if (line is null || line.Quantity < c.Quantity) return Fail<CheckoutResultDto>(400, "Se intentó cobrar más de lo que tiene la cuenta. Actualiza la mesa.");
            line.Quantity -= c.Quantity;
            if (line.Quantity == 0) db.TableOrderLines.Remove(line);
        }
        var closes = order.Lines.All(l => l.Quantity == 0);
        if (closes)
        {
            order.Status = TableOrderStatus.Closed;
            order.ClosedAtUtc = DateTime.UtcNow;
        }

        // SaveChanges de la venta guarda también estos cambios de la cuenta (mismo DbContext, misma transacción)
        var tableName = await db.DiningTables.AsNoTracking().Where(t => t.Id == order.TableId).Select(t => t.Name).FirstAsync(ct);
        var result = await sales.CreateAsync(ToSaleRequest(req, charge.Select(c => new SaleLineRequest(c.ProductId, c.Quantity, c.Note)).ToList()),
            userId, ct, order.Id, tableName);
        if (!result.Ok) return Fail<CheckoutResultDto>(result.Status, result.Error!);
        return ServiceResult<CheckoutResultDto>.Success(new CheckoutResultDto(result.Value!, closes ? null : await LoadDtoAsync(order.Id, ct)), result.Status);
    }

    private static CreateSaleRequest ToSaleRequest(CheckoutOrderRequest r, IReadOnlyList<SaleLineRequest> lines) =>
        new(r.SaleId, r.CashSessionId, r.CreatedAtUtc, r.PaymentMethod, lines, r.Payments);

    // Junta líneas del mismo producto con la misma nota, conservando el orden de aparición
    private static List<(Guid ProductId, int Quantity, string? Note)> Merge(IEnumerable<(Guid ProductId, int Quantity, string? Note)> lines)
    {
        var result = new List<(Guid ProductId, int Quantity, string? Note)>();
        foreach (var l in lines)
        {
            var i = result.FindIndex(r => r.ProductId == l.ProductId && r.Note == l.Note);
            if (i < 0) result.Add(l);
            else result[i] = (l.ProductId, result[i].Quantity + l.Quantity, l.Note);
        }
        return result;
    }

    private async Task<Guid?> OpenOrderIdAsync(Guid tableId, CancellationToken ct) =>
        await db.TableOrders.AsNoTracking().Where(o => o.TableId == tableId && o.Status == TableOrderStatus.Open)
            .Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);

    private async Task<(TableOrder? Order, (int Status, string Message)? Error)> LoadOpenAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var order = await db.TableOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return (null, (404, "Cuenta no encontrada."));
        if (await CheckBranchAccessAsync(order.BranchId, userId, isAdmin, ct) is { } denied) return (null, (403, denied));
        if (order.Status != TableOrderStatus.Open) return (null, (409, "Esa cuenta ya fue cobrada o cerrada. Actualiza las mesas."));
        return (order, null);
    }

    private async Task<string?> ValidateProductsAsync(Guid branchId, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return null;
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (products.Count != ids.Count || products.Any(p => !p.Active)) return "Hay productos inexistentes o inactivos.";
        var today = await settings.TodayAsync(ct);
        var menu = await db.DailyMenuEntries.AsNoTracking().Where(e => e.BranchId == branchId && e.Date == today && ids.Contains(e.ProductId))
            .Select(e => e.ProductId).ToListAsync(ct);
        var missing = products.FirstOrDefault(p => p.IsMenuItem && !menu.Contains(p.Id));
        return missing is null ? null : $"«{missing.Name}» no está en el menú de hoy.";
    }

    // Nombre y precio de hoy (sin IGV) de cada producto: el del menú del día para platos de menú
    private async Task<Dictionary<Guid, (string Name, decimal Price)>> PricesAsync(Guid branchId, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.Price, p.IsMenuItem }).ToListAsync(ct);
        var today = await settings.TodayAsync(ct);
        var menu = await db.DailyMenuEntries.AsNoTracking().Where(e => e.BranchId == branchId && e.Date == today && ids.Contains(e.ProductId))
            .ToDictionaryAsync(e => e.ProductId, e => e.Price, ct);
        return products.ToDictionary(p => p.Id, p => (p.Name, p.IsMenuItem && menu.TryGetValue(p.Id, out var mp) ? mp : p.Price));
    }

    private async Task<TableOrderDto> LoadDtoAsync(Guid orderId, CancellationToken ct)
    {
        var o = await db.TableOrders.AsNoTracking().Include(x => x.Lines).Include(x => x.Table).Include(x => x.OpenedBy)
            .FirstAsync(x => x.Id == orderId, ct);
        var prices = await PricesAsync(o.BranchId, o.Lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        var lines = o.Lines.OrderBy(l => l.Position).Select(l =>
        {
            var (name, price) = prices.TryGetValue(l.ProductId, out var p) ? p : ("(producto)", 0m);
            return new OrderLineDto(l.ProductId, name, price, l.Quantity, l.Note, Money.Round(price * l.Quantity));
        }).ToList();
        return new TableOrderDto(o.Id, o.TableId, o.Table?.Name ?? "", o.OpenedAtUtc, o.OpenedBy?.FullName ?? "", lines, lines.Sum(l => l.LineSubtotal));
    }
}
