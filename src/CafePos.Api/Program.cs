using System.Text;
using System.Text.Json.Serialization;
using CafePos.Api.Auth;
using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Endpoints;
using CafePos.Api.Services;
using CafePos.Api.Support;
using CafePos.Shared;
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
builder.Services.Configure<TaxOptions>(builder.Configuration.GetSection("Tax"));
builder.Services.Configure<BusinessOptions>(builder.Configuration.GetSection("Business"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<CashSessionService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ClockSkew = TimeSpan.FromMinutes(1),
    });

// Con la marca "mcp" (debe cambiar contraseña) solo se puede llamar a change-password ("Authenticated")
static bool NoPendingPasswordChange(AuthorizationHandlerContext c) => !c.User.HasClaim("mcp", "1");
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(NoPendingPasswordChange).Build())
    .AddPolicy("Authenticated", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireRole(Roles.Admin).RequireAssertion(NoPendingPasswordChange));

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi();

var app = builder.Build();

// Migraciones + administrador inicial (debe cambiar la contraseña en el primer ingreso)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Users.AnyAsync())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var seedPassword = builder.Configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(seedPassword) || seedPassword.Length < 8)
            throw new InvalidOperationException("Seed:AdminPassword debe tener al menos 8 caracteres.");
        var admin = new User
        {
            Username = (builder.Configuration["Seed:AdminUser"] ?? "admin").ToLowerInvariant(),
            FullName = "Administrador",
            Role = Roles.Admin,
            MustChangePassword = true
        };
        admin.PasswordHash = hasher.HashPassword(admin, seedPassword);
        db.Users.Add(admin);
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
app.MapSales();
app.MapUsers();
app.MapReports();

app.Run();
