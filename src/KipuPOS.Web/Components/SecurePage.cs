using KipuPOS.Shared;
using KipuPOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace KipuPOS.Web.Components;

// Base de las páginas que exigen sesión: redirige al login, al cambio de contraseña o a la página del rol.
public abstract class SecurePage : ComponentBase
{
    [Inject] protected AuthSession Auth { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected ApiClient Api { get; set; } = default!;

    protected virtual IReadOnlyCollection<string> AllowedRoles => Roles.TenantRoles;
    protected bool Allowed { get; private set; }

    // Usuario al abrir la página: sigue disponible aunque la sesión se cierre mientras la página se redibuja
    protected LoginResponse User { get; private set; } = default!;

    protected override void OnInitialized()
    {
        var s = Auth.Current;
        if (s is null || !Auth.IsAuthenticated) { Nav.NavigateTo("/login"); return; }
        if (s.MustChangePassword) { Nav.NavigateTo("/cambiar-clave"); return; }
        if (!AllowedRoles.Contains(s.Role)) { Nav.NavigateTo(AppRoutes.HomeFor(s)); return; }
        User = s;
        Allowed = true;
    }

    protected async Task LogoutAsync()
    {
        await Auth.ClearAsync();
        Nav.NavigateTo("/login");
    }
}

public static class AppRoutes
{
    public static string HomeFor(LoginResponse s) => s.Role switch
    {
        Roles.Platform => "/plataforma",
        Roles.Admin => "/admin/menu",
        _ => "/caja"
    };
}
