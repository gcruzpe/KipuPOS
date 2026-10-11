using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Endpoints;

// Logos del negocio: los sube el administrador (o la plataforma al dar de alta) y se leen sin token para el login.
public static class BrandingEndpoints
{
    public const int MaxLogoBytes = 512 * 1024;

    public static void MapBranding(this IEndpointRouteBuilder app)
    {
        // Público: el login muestra la marca apenas se escribe el código. Solo negocios activos; no expone nada más que nombre y logo.
        var pub = app.MapGroup("/api/public/tenants/{code}").AllowAnonymous();

        pub.MapGet("/branding", async (string code, AppDbContext db, CancellationToken ct) =>
        {
            var c = Normalize(code);
            return await BrandingAsync(db, db.Tenants.Where(t => t.Code == c && t.Active), ct) is { } b ? Results.Ok(b) : Results.NotFound();
        });

        pub.MapGet("/{kind}", async (string code, string kind, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (!LogoKinds.All.Contains(kind)) return Results.NotFound();
            var c = Normalize(code);
            var asset = await db.TenantAssets.AsNoTracking()
                .Where(a => a.Kind == kind && db.Tenants.Any(t => t.Id == a.TenantId && t.Code == c && t.Active))
                .Select(a => new { a.ContentType, a.Data })
                .FirstOrDefaultAsync(ct);
            if (asset is null) return Results.NotFound();

            // La URL lleva ?v=<versión>, así que se puede cachear sin miedo a mostrar un logo viejo
            http.Response.Headers.CacheControl = "public, max-age=86400";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(asset.Data, asset.ContentType);
        });

        // Negocio del usuario autenticado
        app.MapGet("/api/tenant/branding", async (ITenantContext tc, AppDbContext db, CancellationToken ct) =>
        {
            var id = tc.TenantId;
            return await BrandingAsync(db, db.Tenants.Where(t => t.Id == id), ct) is { } b ? Results.Ok(b) : Results.NotFound();
        }).RequireAuthorization();

        var admin = app.MapGroup("/api/tenant").RequireAuthorization("Admin");
        admin.MapPut("/{kind}", (string kind, SetLogoRequest r, ITenantContext tc, AppDbContext db, CancellationToken ct) =>
            SetAsync(db, tc.TenantId, kind, r, ct));
        admin.MapDelete("/{kind}", (string kind, ITenantContext tc, AppDbContext db, CancellationToken ct) =>
            DeleteAsync(db, tc.TenantId, kind, ct));

        // Plataforma: puede dejar el logo listo al dar de alta un negocio
        var platform = app.MapGroup("/api/platform/tenants/{id:guid}").RequireAuthorization("Platform");
        platform.MapPut("/{kind}", (Guid id, string kind, SetLogoRequest r, AppDbContext db, CancellationToken ct) =>
            SetAsync(db, id, kind, r, ct));
        platform.MapDelete("/{kind}", (Guid id, string kind, AppDbContext db, CancellationToken ct) =>
            DeleteAsync(db, id, kind, ct));
    }

    private static string Normalize(string code) => (code ?? "").Trim().ToLowerInvariant();

    private static async Task<TenantBrandingDto?> BrandingAsync(AppDbContext db, IQueryable<Tenant> query, CancellationToken ct)
    {
        var t = await query.AsNoTracking().Select(x => new { x.Id, x.Code, x.Name }).FirstOrDefaultAsync(ct);
        if (t is null) return null;
        var assets = await db.TenantAssets.AsNoTracking().Where(a => a.TenantId == t.Id)
            .Select(a => new { a.Kind, a.UpdatedAtUtc }).ToListAsync(ct);
        long? Version(string kind) => assets.FirstOrDefault(a => a.Kind == kind)?.UpdatedAtUtc.Ticks;
        return new TenantBrandingDto(t.Code, t.Name, Version(LogoKinds.Light), Version(LogoKinds.Dark));
    }

    private static async Task<IResult> SetAsync(AppDbContext db, Guid? tenantId, string kind, SetLogoRequest r, CancellationToken ct)
    {
        if (!LogoKinds.All.Contains(kind)) return Results.NotFound();
        if (tenantId is not { } tid || !await db.Tenants.AnyAsync(t => t.Id == tid, ct)) return Results.NotFound();

        byte[] data;
        try { data = Convert.FromBase64String(r.DataBase64 ?? ""); }
        catch (FormatException) { return Results.BadRequest("La imagen no es válida."); }
        if (data.Length == 0) return Results.BadRequest("La imagen está vacía.");
        if (data.Length > MaxLogoBytes) return Results.BadRequest("El logo debe pesar como máximo 512 KB.");

        // El tipo se deduce del contenido, no de lo que diga el cliente (SVG no se acepta: puede llevar scripts)
        var contentType = Sniff(data);
        if (contentType is null) return Results.BadRequest("Formato no soportado: usa PNG, JPG o WebP.");

        var asset = await db.TenantAssets.FirstOrDefaultAsync(a => a.TenantId == tid && a.Kind == kind, ct);
        if (asset is null) db.TenantAssets.Add(asset = new TenantAsset { TenantId = tid, Kind = kind });
        asset.ContentType = contentType;
        asset.Data = data;
        asset.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await BrandingAsync(db, db.Tenants.Where(t => t.Id == tid), ct));
    }

    private static async Task<IResult> DeleteAsync(AppDbContext db, Guid? tenantId, string kind, CancellationToken ct)
    {
        if (!LogoKinds.All.Contains(kind) || tenantId is not { } tid) return Results.NotFound();
        await db.TenantAssets.Where(a => a.TenantId == tid && a.Kind == kind).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static string? Sniff(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
            return "image/webp";
        return null;
    }
}
