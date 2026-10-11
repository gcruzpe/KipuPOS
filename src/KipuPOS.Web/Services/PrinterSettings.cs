using Microsoft.JSInterop;

namespace KipuPOS.Web.Services;

// Impresora de tickets de ESTE equipo (cada caja puede tener la suya): se guarda en localStorage, no en el servidor.
public sealed class PrinterSettings(IJSRuntime js)
{
    private const string WidthKey = "kipupos.printer.width";
    private const string AutoKey = "kipupos.printer.auto";

    public int WidthMm { get; private set; } = 80;   // 58 u 80
    public bool AutoPrint { get; private set; }
    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (int.TryParse(await js.InvokeAsync<string?>("localStorage.getItem", WidthKey), out var w) && w is 58 or 80) WidthMm = w;
            AutoPrint = await js.InvokeAsync<string?>("localStorage.getItem", AutoKey) == "1";
        }
        catch (Exception) { /* sin almacenamiento: valores por defecto */ }
    }

    public async Task SetWidthAsync(int mm)
    {
        WidthMm = mm is 58 ? 58 : 80;
        await TryAsync(() => js.InvokeVoidAsync("localStorage.setItem", WidthKey, WidthMm.ToString()));
    }

    public async Task SetAutoPrintAsync(bool on)
    {
        AutoPrint = on;
        await TryAsync(() => js.InvokeVoidAsync("localStorage.setItem", AutoKey, on ? "1" : "0"));
    }

    // El ticket ya debe estar dibujado en la página (componente TicketPrint); el CSS de impresión oculta todo lo demás
    public async Task PrintAsync()
    {
        await Task.Delay(60); // deja que Blazor termine de pintar el ticket antes de abrir el diálogo
        await TryAsync(() => js.InvokeVoidAsync("print"));
    }

    private static async Task TryAsync(Func<ValueTask> action)
    {
        try { await action(); } catch (Exception) { }
    }
}
