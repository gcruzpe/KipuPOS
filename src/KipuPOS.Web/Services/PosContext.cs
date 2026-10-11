using KipuPOS.Shared;

namespace KipuPOS.Web.Services;

// Datos del negocio que casi no cambian (IGV, zona horaria, sucursales, logos); se cargan una vez por sesión.
public sealed class PosContext(ApiClient api, AuthSession auth)
{
    public TenantDto? Tenant { get; private set; }
    public TenantBrandingDto? Branding { get; private set; }
    public IReadOnlyList<BranchDto> Branches { get; private set; } = [];
    private string? _loadedFor;

    public decimal IgvRate => Tenant?.IgvRate ?? 0.18m;
    public bool HasLogo => Branding is { LogoVersion: not null } or { LogoDarkVersion: not null };

    public async Task EnsureLoadedAsync()
    {
        var token = auth.Current?.Token;
        if (token is null || _loadedFor == token) return;
        Tenant = await api.GetAsync<TenantDto>("api/tenant");
        Branches = await api.GetAsync<List<BranchDto>>("api/branches") ?? [];
        Branding = await api.GetAsync<TenantBrandingDto>("api/tenant/branding");
        _loadedFor = token;
    }

    public void SetBranding(TenantBrandingDto? branding) => Branding = branding;

    public string? BranchName(Guid? id) => id is null ? null : Branches.FirstOrDefault(b => b.Id == id)?.Name;

    // Hora local del negocio (la API guarda todo en UTC)
    public DateTime ToLocal(DateTime utc) => utc.AddHours(Tenant?.UtcOffsetHours ?? -5);
    public DateOnly Today => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow));
}
