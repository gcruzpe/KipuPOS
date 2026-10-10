using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Services;

// El menú del día es por sucursal. El cajero trabaja siempre con la suya;
// el administrador usa la suya o indica otra con ?branchId=.
public class MenuService(AppDbContext db, TenantSettings settings, ITenantContext tenant)
{
    private async Task<(Guid? Id, string? Error)> ResolveBranchAsync(Guid? requested, bool isAdmin, CancellationToken ct)
    {
        var id = isAdmin ? requested ?? tenant.BranchId : tenant.BranchId;
        if (id is null) return (null, "Indica la sucursal (branchId).");
        if (!await db.Branches.AnyAsync(b => b.Id == id, ct)) return (null, "Sucursal no encontrada.");
        return (id, null);
    }

    private async Task<DailyMenuDto> LoadAsync(DateOnly date, Guid branchId, CancellationToken ct)
    {
        var entries = await db.DailyMenuEntries.AsNoTracking()
            .Where(e => e.Date == date && e.BranchId == branchId).OrderBy(e => e.SortOrder)
            .Select(e => new MenuEntryDto(e.Id, e.ProductId, e.Product!.Name, e.Price, e.SoldOut)).ToListAsync(ct);
        return new DailyMenuDto(date, entries);
    }

    // Menú de hoy (fecha del negocio). Lista vacía si aún no se cargó.
    public async Task<ServiceResult<DailyMenuDto>> TodayAsync(Guid? branchId, bool isAdmin, CancellationToken ct)
    {
        var (branch, error) = await ResolveBranchAsync(branchId, isAdmin, ct);
        if (branch is null) return ServiceResult<DailyMenuDto>.Fail(400, error!);
        return ServiceResult<DailyMenuDto>.Success(await LoadAsync(await settings.TodayAsync(ct), branch.Value, ct));
    }

    public async Task<ServiceResult<DailyMenuDto>> GetAsync(DateOnly date, Guid? branchId, CancellationToken ct)
    {
        var (branch, error) = await ResolveBranchAsync(branchId, isAdmin: true, ct);
        if (branch is null) return ServiceResult<DailyMenuDto>.Fail(400, error!);
        return ServiceResult<DailyMenuDto>.Success(await LoadAsync(date, branch.Value, ct));
    }

    // Reemplaza el menú completo de una fecha y sucursal
    public async Task<ServiceResult<DailyMenuDto>> SetAsync(DateOnly date, Guid? branchId, IReadOnlyList<MenuEntryUpsert>? entries, CancellationToken ct)
    {
        var (branch, error) = await ResolveBranchAsync(branchId, isAdmin: true, ct);
        if (branch is null) return ServiceResult<DailyMenuDto>.Fail(400, error!);
        if (entries is null) return ServiceResult<DailyMenuDto>.Fail(400, "Falta la lista de platos.");
        if (entries.Count > 20) return ServiceResult<DailyMenuDto>.Fail(400, "Máximo 20 platos por menú.");
        if (entries.GroupBy(e => e.ProductId).Any(g => g.Count() > 1)) return ServiceResult<DailyMenuDto>.Fail(400, "Hay platos repetidos.");
        if (entries.Any(e => e.Price < 0 || e.Price > 1000)) return ServiceResult<DailyMenuDto>.Fail(400, "Precio inválido.");

        var ids = entries.Select(e => e.ProductId).ToList();
        var valid = await db.Products.CountAsync(p => ids.Contains(p.Id) && p.IsMenuItem && p.Active, ct);
        if (valid != ids.Count) return ServiceResult<DailyMenuDto>.Fail(400, "Solo se pueden usar platos activos marcados como de menú.");

        var existing = await db.DailyMenuEntries.Where(e => e.Date == date && e.BranchId == branch.Value).ToListAsync(ct);
        foreach (var old in existing.Where(o => !ids.Contains(o.ProductId))) db.DailyMenuEntries.Remove(old);

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var row = existing.FirstOrDefault(x => x.ProductId == e.ProductId);
            if (row is null)
            {
                row = new DailyMenuEntry { Date = date, BranchId = branch.Value, ProductId = e.ProductId };
                db.DailyMenuEntries.Add(row);
            }
            row.Price = Money.Round(e.Price);
            row.SoldOut = e.SoldOut;
            row.SortOrder = i;
        }
        await db.SaveChangesAsync(ct);
        return ServiceResult<DailyMenuDto>.Success(await LoadAsync(date, branch.Value, ct));
    }

    // "Copiar de ayer": no sobrescribe un menú ya cargado
    public async Task<ServiceResult<DailyMenuDto>> CopyAsync(DateOnly target, DateOnly source, Guid? branchId, CancellationToken ct)
    {
        var (branch, error) = await ResolveBranchAsync(branchId, isAdmin: true, ct);
        if (branch is null) return ServiceResult<DailyMenuDto>.Fail(400, error!);
        if (target == source) return ServiceResult<DailyMenuDto>.Fail(400, "Las fechas deben ser distintas.");
        if (await db.DailyMenuEntries.AnyAsync(e => e.Date == target && e.BranchId == branch.Value, ct))
            return ServiceResult<DailyMenuDto>.Fail(409, "Ya hay un menú cargado para esa fecha.");

        var src = await db.DailyMenuEntries.AsNoTracking()
            .Where(e => e.Date == source && e.BranchId == branch.Value && e.Product!.Active).OrderBy(e => e.SortOrder).ToListAsync(ct);
        if (src.Count == 0) return ServiceResult<DailyMenuDto>.Fail(404, "No hay menú en la fecha de origen.");

        var i = 0;
        foreach (var s in src)
            db.DailyMenuEntries.Add(new DailyMenuEntry { Date = target, BranchId = branch.Value, ProductId = s.ProductId, Price = s.Price, SortOrder = i++ });
        await db.SaveChangesAsync(ct);
        return ServiceResult<DailyMenuDto>.Success(await LoadAsync(target, branch.Value, ct), 201);
    }

    // Admin: cualquier plato del negocio. Cajero: solo platos de hoy de su sucursal.
    public async Task<ServiceResult<MenuEntryDto>> SetSoldOutAsync(Guid id, bool soldOut, bool isAdmin, CancellationToken ct)
    {
        var e = await db.DailyMenuEntries.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return ServiceResult<MenuEntryDto>.Fail(404, "Plato del menú no encontrado.");
        if (!isAdmin && (e.BranchId != tenant.BranchId || e.Date != await settings.TodayAsync(ct)))
            return ServiceResult<MenuEntryDto>.Fail(403, "Solo puedes marcar agotados los platos de hoy de tu sucursal.");
        e.SoldOut = soldOut;
        await db.SaveChangesAsync(ct);
        return ServiceResult<MenuEntryDto>.Success(new MenuEntryDto(e.Id, e.ProductId, e.Product!.Name, e.Price, e.SoldOut));
    }
}
