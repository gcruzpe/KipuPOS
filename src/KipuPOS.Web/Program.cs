using KipuPOS.Web;
using KipuPOS.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// La API vive en otro origen (ver Cors:Origins en la API); por defecto, el mismo servidor del frontend
var apiBase = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<PosContext>();

var host = builder.Build();
await host.Services.GetRequiredService<AuthSession>().InitializeAsync();
await host.RunAsync();
