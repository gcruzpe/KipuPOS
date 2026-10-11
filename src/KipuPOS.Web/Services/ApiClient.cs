using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace KipuPOS.Web.Services;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public bool IsNetwork => Status == 0;
}

// Envuelve HttpClient: agrega el token, usa enums como texto (igual que la API) y traduce los errores a mensajes legibles.
public sealed class ApiClient(HttpClient http, AuthSession auth, NavigationManager nav)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // URL pública de un logo del negocio; la versión evita que el navegador muestre uno viejo de su caché
    public string LogoUrl(string tenantCode, string kind, long version) =>
        new Uri(http.BaseAddress!, $"api/public/tenants/{Uri.EscapeDataString(tenantCode)}/{kind}?v={version}").ToString();

    public Task<T?> GetAsync<T>(string url, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, url, null, ct);
    public Task<T?> DeleteAsync<T>(string url, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Delete, url, null, ct);
    public Task<T?> PostAsync<T>(string url, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Post, url, body, ct);
    public Task<T?> PutAsync<T>(string url, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, url, body, ct);
    public Task<T?> PatchAsync<T>(string url, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Patch, url, body, ct);

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        if (auth.Current is { } session) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(0, "No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.");
        }

        using (res)
        {
            // Token vencido o revocado: se cierra la sesión (el login maneja su propio 401)
            if (res.StatusCode == HttpStatusCode.Unauthorized && auth.Current is not null)
            {
                await auth.ClearAsync();
                nav.NavigateTo("/login");
                throw new ApiException(401, "Tu sesión expiró. Vuelve a iniciar sesión.");
            }
            if (!res.IsSuccessStatusCode) throw new ApiException((int)res.StatusCode, await ReadErrorAsync(res, ct));
            if (res.StatusCode == HttpStatusCode.NoContent) return default;

            var text = await res.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
        }
    }

    // La API responde ProblemDetails ({ detail }) o un texto JSON ("mensaje") según el endpoint
    private static async Task<string> ReadErrorAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var status = (int)res.StatusCode;
        try
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String) return root.GetString()!;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) return d.GetString()!;
                    if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Object)
                        foreach (var e in errs.EnumerateObject())
                            if (e.Value.ValueKind == JsonValueKind.Array && e.Value.GetArrayLength() > 0)
                                return e.Value[0].GetString() ?? "Datos inválidos.";
                }
            }
        }
        catch (JsonException) { /* cuerpo que no es JSON: mensaje genérico */ }

        return status switch
        {
            400 => "Los datos enviados no son válidos.",
            403 => "No tienes permiso para hacer esto.",
            404 => "No se encontró lo que buscabas.",
            409 => "La operación entra en conflicto con otro cambio. Actualiza e inténtalo de nuevo.",
            _ => $"El servidor respondió con un error ({status}). Inténtalo de nuevo."
        };
    }
}
