using System.Text;
using System.Text.Json.Serialization;
using KipuPOS.Api.Auth;
using KipuPOS.Api.Data;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Endpoints;
using KipuPOS.Api.Services;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Los enums viajan como texto ("Efectivo", "Valid"); los clientes deben usar JsonStringEnumConverter
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// Multi-tenencia: el negocio sale del token; IGV y zona horaria salen del negocio
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<TenantSettings>();
builder.Services.AddScoped<DocumentNumberService>();

builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<CashSessionService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<TableOrderService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Sin esto el handler renombra "tid" al claim de tenant de Azure AD y el negocio desaparece del usuario (403 en todo)
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

// Con la marca "mcp" (debe cambiar contraseña) solo se puede llamar a change-password ("Authenticated").
// Los endpoints de un negocio exigen token con negocio; los de plataforma, rol Platform.
static bool NoPendingPasswordChange(AuthorizationHandlerContext c) => !c.User.HasClaim(AppClaims.MustChangePassword, "1");
static bool InTenant(AuthorizationHandlerContext c) => c.User.HasClaim(x => x.Type == AppClaims.Tenant);
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
        .RequireAssertion(NoPendingPasswordChange).RequireAssertion(InTenant).Build())
    .AddPolicy("Authenticated", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireRole(Roles.Admin).RequireAssertion(NoPendingPasswordChange).RequireAssertion(InTenant))
    .AddPolicy("Platform", p => p.RequireRole(Roles.Platform).RequireAssertion(NoPendingPasswordChange));

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi();

var app = builder.Build();

// Migraciones + primer usuario de plataforma (no se ejecuta cuando lo invoca `dotnet ef`)
if (!EF.IsDesignTime)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Role == Roles.Platform))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var seedPassword = builder.Configuration["Seed:PlatformPassword"];
        if (string.IsNullOrEmpty(seedPassword) || seedPassword.Length < 8)
            throw new InvalidOperationException("Seed:PlatformPassword debe tener al menos 8 caracteres.");

        var platform = new User
        {
            Username = (builder.Configuration["Seed:PlatformUser"] ?? "platform").Trim().ToLowerInvariant(),
            FullName = "Plataforma", Role = Roles.Platform, MustChangePassword = true
        };
        platform.PasswordHash = hasher.HashPassword(platform, seedPassword);
        db.Users.Add(platform);
        await db.SaveChangesAsync();
    }
}

app.UseSerilogRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();
app.MapAuth();
app.MapCatalog();
app.MapSessions();
app.MapMenu();
app.MapSales();
app.MapTables();
app.MapUsers();
app.MapTenantAdmin();
app.MapBranding();
app.MapPlatform();
app.MapReports();

app.Run();
