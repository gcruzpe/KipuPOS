# KipuPOS

Punto de venta en la nube **multinegocio** (tenants con una o más sucursales), pensado para cafeterías y restaurantes en Perú.
El usuario trabaja en español: responder, comentar el código y escribir textos de interfaz en español.

Primer cliente real: **Tostadores Café** (código `tostadores-cafe`). Sus logos ya preparados están en `assets/clientes/tostadores-cafe/`.

## Estructura
- `src/KipuPOS.Api` – ASP.NET Core 10, minimal APIs, EF Core + SQL Server (LocalDB en desarrollo), JWT. Puerto `https://localhost:7100`.
- `src/KipuPOS.Shared` – DTOs compartidos (records) entre API y frontend. Cambiar un DTO afecta a ambos.
- `src/KipuPOS.Web` – Blazor WebAssembly standalone. Puerto `https://localhost:7200` (origen permitido en `Cors:Origins` de la API).
- `tools/seed-demo.py` – crea datos de demo en la API local (negocio, catálogo de ejemplo, menú de hoy, cajero, logos). Idempotente.
- `KipuPOS.http` – flujo completo de la API para probar a mano.
- `README.md` – endpoints, reglas de negocio y multi-tenencia. **Leerlo antes de tocar la API**; no duplicar aquí su contenido.

## Comandos
```
dotnet build KipuPOS.slnx
dotnet run --project src/KipuPOS.Api --urls "https://localhost:7100;http://localhost:5100"
dotnet run --project src/KipuPOS.Web --launch-profile https
python tools/seed-demo.py                     # negocio kipu-demo
python tools/seed-demo.py --tenant tostadores-cafe --name "Tostadores Café" --branch "Principal" --logos assets/clientes/tostadores-cafe
dotnet ef migrations add <Nombre> -p src/KipuPOS.Api --configuration Release   # Release evita choques con una API abierta en VS
```
- La API aplica migraciones y crea el usuario de plataforma al arrancar.
- Usuarios de prueba y contraseñas: constantes al inicio de `tools/seed-demo.py`. No repetirlas en el chat; apuntar a ese archivo.
- Visual Studio con la API corriendo bloquea `bin/Debug`: compilar a otra salida (`-o`) o en Release. No lanzar dos `dotnet run` en paralelo: chocan al compilar `KipuPOS.Shared`.
- Puertos fijos sin depender del perfil: API en `appsettings.Development.json` (`Urls`), Web con `_RunExtraArguments --urls` en el csproj.
- **Probar sin tocar la base ni el VS del usuario:** instancia aparte `sqllocaldb create KipuTest -s`, API con `ConnectionStrings__Default=Server=(localdb)\KipuTest;Database=KipuPOS_Test;...` y `--urls http://localhost:5110`, frontend con `-p:WasmApplicationEnvironmentName=Test -p:_RunExtraArguments="--urls http://localhost:5300"` y un `wwwroot/appsettings.Test.json` temporal. Al terminar: DROP DATABASE, `sqllocaldb delete KipuTest` y borrar el json. (En .NET 10 el entorno del WASM se fija al compilar; el encabezado del servidor no lo cambia.)
- LocalDB ("SQL Server process failed to start"): una instancia arrancada desde la consola de Claude no la ve Visual Studio, que intenta arrancar otra con los mismos archivos y falla. **Al terminar las pruebas, detenerla con `sqllocaldb stop MSSQLLocalDB`** (si quedó colgada: `-k`) y no usar `sqllocaldb start`; que la arranque la propia API desde VS.

## Reglas que no se negocian
- **Multi-tenencia** (detalle en README): el negocio sale siempre del token (claim `tid`), nunca del cuerpo. Filtros globales de EF; no usar `FindAsync`; SQL manual debe filtrar `TenantId` a mano.
- **`MapInboundClaims = false`** en `AddJwtBearer` (Program.cs). Sin eso el handler renombra `tid` al claim de Azure AD y todo usuario de negocio recibe 403.
- Precios de producto **sin IGV**; el IGV es por negocio (`Tenant.IgvRate`). Redondeo a 2 decimales `AwayFromZero`.
- Ventas: idempotentes por `Id` generado en el cliente; anuladas nunca se borran.
- Pago mixto: las partes viven en `SalePayments`. Todo cálculo por forma de pago (cierre, reportes) se hace sobre las partes, no sobre `Sale.PaymentMethod`.
- Mesas: `TableOrderService` deja pendiente en el DbContext el descuento de la cuenta y `SaleService.CreateAsync` lo guarda en la misma transacción que la venta. No separar esos dos pasos.
- Enums viajan como texto: el cliente usa `JsonStringEnumConverter` (ver `ApiClient.Json`).
- Logos: tabla `TenantAsset`, tipos `logo` (fondos claros) y `logo-dark` (fondos oscuros). Solo PNG/JPG/WebP ≤ 512 KB, tipo deducido del contenido; **SVG no** (puede llevar scripts).

## Frontend (KipuPOS.Web)
- Páginas en `Pages/`; las que exigen sesión heredan de `Components/SecurePage` (redirige por rol y expone `User`). Usar `User`, no `Auth.Current!`, en el marcado (al cerrar sesión la página se redibuja sin sesión).
- `Services/ApiClient` traduce errores (ProblemDetails o texto) a `ApiException` con mensaje legible; un 401 cierra la sesión.
- `Services/PosContext` cachea negocio, sucursales y marca por sesión (`EnsureLoadedAsync`).
- Componentes de marca: `Logo` (KipuPOS), `TenantMark` (logo del negocio con placa de respaldo), `PosHeader`, `AdminShell`.
- URL de la API en `wwwroot/appsettings.json` (`ApiBaseUrl`). Un `appsettings.Development.json` con otra URL es solo para pruebas locales: **no hacer commit**.

## Estilo visual: "Andina contemporánea"
Elegido por el usuario. Mantener en toda pantalla nueva; tokens en `wwwroot/css/app.css`.
- Fondo `#F4ECE0`, superficies `#FFFBF4` / `#FFFDF8`, tinta `#2B1D14`, texto secundario `#6E5A48`, bordes suaves `#CDB89C` (separadores punteados).
- Acentos: grana `#9E1F45` (acción principal), ocre `#C98A12`, verde `#3F6B3A`, índigo `#3A3F7A`; sobre oscuro `#E7B54A`, `#E04B76`, `#8DBA7F`.
- Tipografía: Bricolage Grotesque (títulos 800, tracking negativo) + DM Mono (precios y números).
- Formas: bordes de 2 px en tinta, tarjetas con radio `4px 4px 18px 18px` y banda de color arriba, botones píldora.
- Franja "tejido" de colores bajo los encabezados y junto al menú lateral; logo de quipu con cordones de colores.
- Lema del login: **"Hilos de colores que cuentan tu historia, venta a venta."** (descartado "nudo": suena a problema).
- Con logo del negocio, su marca manda en el encabezado y KipuPOS queda como marca pequeña.
- Maqueta de referencia (claude.ai): https://claude.ai/artifact/8SLpjoJRmBMycXFTKeLc8n

## Flujo de trabajo
- Repositorio: https://github.com/gcruzpe/KipuPOS (rama `main`). Una rama por funcionalidad, PR y merge.
- Antes de commit: compilar la solución sin errores ni advertencias y quitar archivos temporales (`appsettings.Development.json`, `__pycache__`).
- En PowerShell 5.1 los mensajes de commit multilínea se rompen: escribir el mensaje en un archivo y usar `git commit -F`.
- Para ver la app en el navegador integrado de Claude: ese panel no confía en el certificado HTTPS de desarrollo; usar HTTP (frontend `http://localhost:5200`, API por HTTP con `http://localhost:5200` agregado a `Cors:Origins`).

## Pendientes
Ver `ROADMAP.md`: fases, tareas con casillas y datos que faltan del cliente. Al terminar una tarea, marcarla ahí.
Ticket impreso: `Components/TicketPrint.razor` + reglas `@media print` en `app.css` (72 mm imprimibles en papel de 80 mm; impresora de Tostadores: Epson TM-T88V). Preferencias por equipo en `Services/PrinterSettings` (localStorage).
En Visual Studio se arranca todo con el perfil "KipuPOS (API + Web)" de `KipuPOS.slnLaunch`.
