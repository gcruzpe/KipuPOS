using System.Security.Claims;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Support;

// Negocio y sucursal de quien hace la solicitud (salen del token JWT, nunca del cuerpo de la petición)
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? BranchId { get; }
}

public class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId => Read("tid");
    public Guid? BranchId => Read("bid");

    private Guid? Read(string claim) =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(claim), out var id) ? id : null;
}

// Ajustes del negocio actual (IGV, zona horaria). Se leen una sola vez por solicitud.
public class TenantSettings(AppDbContext db, ITenantContext ctx)
{
    private Tenant? _tenant;

    public async Task<Tenant> GetAsync(CancellationToken ct)
    {
        if (_tenant is not null) return _tenant;
        var id = ctx.TenantId ?? throw new InvalidOperationException("No hay un negocio en el contexto actual.");
        _tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == id, ct);
        return _tenant;
    }

    // Fecha de negocio de hoy (hora local del negocio)
    public async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddHours((await GetAsync(ct)).UtcOffsetHours));
}
