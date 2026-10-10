using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Services;

public class DocumentNumberService(AppDbContext db, ITenantContext tenant)
{
    // Debe llamarse DENTRO de una transacción. El UPDATE bloquea la fila del contador hasta el commit:
    // dos ventas simultáneas de la misma sucursal no obtienen el mismo número, y si la venta falla
    // el rollback devuelve el número (sin huecos en la numeración).
    public async Task<long> NextAsync(Guid branchId, CancellationToken ct, string type = DocumentTypes.Ticket)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No hay negocio en contexto.");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [DocumentCounters] SET [LastNumber] = [LastNumber] + 1 WHERE [TenantId] = {tenantId} AND [BranchId] = {branchId} AND [Type] = {type}",
                ct);

            if (rows == 1)
                return await db.DocumentCounters.AsNoTracking()
                    .Where(c => c.BranchId == branchId && c.Type == type)
                    .Select(c => c.LastNumber).SingleAsync(ct);

            // Normalmente el contador se crea junto con la sucursal; esto es solo una red de seguridad
            db.DocumentCounters.Add(new DocumentCounter { TenantId = tenantId, BranchId = branchId, Type = type });
            await db.SaveChangesAsync(ct);
        }
        throw new InvalidOperationException("No se pudo obtener el número de documento.");
    }
}
