using System.Text.Json;
using KipuPOS.Shared;
using Microsoft.JSInterop;

namespace KipuPOS.Web.Services;

// Sesión del usuario: el token y sus datos viven en localStorage para sobrevivir a una recarga de la caja.
public sealed class AuthSession(IJSRuntime js)
{
    private const string SessionKey = "kipupos.session";
    private const string TenantKey = "kipupos.tenant";

    public LoginResponse? Current { get; private set; }
    public bool IsAuthenticated => Current is not null && Current.ExpiresAtUtc > DateTime.UtcNow;
    public bool IsPlatform => Current?.Role == Roles.Platform;
    public bool IsAdmin => Current?.Role == Roles.Admin;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", SessionKey);
            if (string.IsNullOrEmpty(json)) return;
            var saved = JsonSerializer.Deserialize<LoginResponse>(json, ApiClient.Json);
            if (saved is not null && saved.ExpiresAtUtc > DateTime.UtcNow) Current = saved;
            else await js.InvokeVoidAsync("localStorage.removeItem", SessionKey);
        }
        catch (Exception)
        {
            // Almacenamiento bloqueado o datos corruptos: se empieza sin sesión
            Current = null;
        }
    }

    public async Task SetAsync(LoginResponse session)
    {
        Current = session;
        await TryAsync(() => js.InvokeVoidAsync("localStorage.setItem", SessionKey, JsonSerializer.Serialize(session, ApiClient.Json)));
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        Current = null;
        await TryAsync(() => js.InvokeVoidAsync("localStorage.removeItem", SessionKey));
        Changed?.Invoke();
    }

    // El código de negocio se recuerda para no escribirlo en cada inicio de sesión
    public async Task<string> GetLastTenantAsync()
    {
        try { return await js.InvokeAsync<string?>("localStorage.getItem", TenantKey) ?? ""; }
        catch (Exception) { return ""; }
    }

    public Task SetLastTenantAsync(string code) =>
        TryAsync(() => js.InvokeVoidAsync("localStorage.setItem", TenantKey, code));

    private static async Task TryAsync(Func<ValueTask> action)
    {
        try { await action(); } catch (Exception) { /* sin almacenamiento la sesión dura lo que la pestaña */ }
    }
}
