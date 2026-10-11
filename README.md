# KipuPOS – API (multi-negocio)

Punto de venta en la nube para varios negocios (tenants), cada uno con una o más sucursales.

## Puesta en marcha
```
dotnet new sln -n KipuPOS
dotnet sln add src/KipuPOS.Shared src/KipuPOS.Api
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial -p src/KipuPOS.Api      # borra antes cualquier carpeta Migrations de versiones anteriores
dotnet run --project src/KipuPOS.Api --urls "https://localhost:7100;http://localhost:5100"
```
Si ya tenías una base de datos de la versión anterior, bórrala: el esquema cambió (sin datos de producción todavía).

Al arrancar se aplican las migraciones y se crea **un usuario de plataforma** (`Seed:PlatformUser` / `Seed:PlatformPassword`).
Con ese usuario se crean los negocios (clientes). Flujo completo en `KipuPOS.http`.

En producción, por variables de entorno: `Jwt__Key`, `ConnectionStrings__Default`, `Seed__PlatformPassword`, `Cors__Origins__0`.

## Usar desde Visual Studio
1. Abre `KipuPOS.slnx`.
2. En la barra de herramientas, junto al botón ▶, elige el perfil **"KipuPOS (API + Web)"** (definido en `KipuPOS.slnLaunch`) y pulsa ▶ (F5).
   Arrancan la API (`https://localhost:7100`) y el frontend (`https://localhost:7200`), y se abre el navegador en el login.
3. Si no aparece el perfil: clic derecho en la solución → *Configurar proyectos de inicio* → *Varios proyectos de inicio* → `KipuPOS.Api` y `KipuPOS.Web` en **Iniciar**.
4. La primera vez, si Windows pregunta por el certificado de desarrollo HTTPS, acéptalo (`dotnet dev-certs https --trust`).
   Los puertos están fijos en el proyecto (API: `appsettings.Development.json`; Web: `--urls` en `KipuPOS.Web.csproj`), así que no dependen del perfil.
   Si aparece *"address already in use"*, queda un proceso de una depuración anterior: detén la depuración (Mayús+F5) o ciérralo en el Administrador de tareas.
   Si la API no conecta con la base (*"LocalDB… SQL Server process failed to start"*): `sqllocaldb stop MSSQLLocalDB -k` y vuelve a pulsar F5 (la API arranca LocalDB sola).
5. Datos de prueba: con la API corriendo, `python tools/seed-demo.py` (ver opciones en el archivo).

## Impresora de tickets
- Probado para **Epson TM-T88V** (papel de 80 mm, área imprimible 72 mm). También admite 58 mm (48 mm imprimibles).
- El ancho y la impresión automática al cobrar se eligen en **Ventas** → *Impresora de este equipo* (se guarda por equipo).
- En Windows instala el driver de Epson (APD) con papel "Roll Paper 80 x 297 mm" y corte al final del documento.
  En el diálogo de impresión del navegador: esa impresora, márgenes **Ninguno** y sin encabezados ni pies de página.

## Frontend (Blazor WebAssembly) – `src/KipuPOS.Web`
```
dotnet run --project src/KipuPOS.Api --urls "https://localhost:7100;http://localhost:5100"
dotnet run --project src/KipuPOS.Web --launch-profile https      # https://localhost:7200 (origen permitido en Cors:Origins)
python tools/seed-demo.py                                       # opcional: negocio "kipu-demo" con catálogo, menú de hoy y cajero
```
- La URL de la API está en `wwwroot/appsettings.json` (`ApiBaseUrl`). Reutiliza los DTOs de `KipuPOS.Shared`.
- Pantallas: inicio de sesión, cambio de contraseña obligatorio, caja (abrir turno, menú del día, para llevar con táper, cobro idempotente, ticket impreso), ventas del turno (reimprimir con marca COPIA, anular con motivo), cierre de turno con arqueo; Admin: menú del día, catálogo, usuarios, sucursales, ventas de hoy, mi negocio; Plataforma: negocios.
- Estilo "Andina contemporánea": tokens en `wwwroot/css/app.css` (`--grana`, `--ocre`, `--verde`, `--indigo`…), fuentes Bricolage Grotesque + DM Mono.
- El token se guarda en `localStorage`; un 401 cierra la sesión y vuelve al login.
- Marca del negocio: cada negocio sube dos logos en **Mi negocio** — `logo` (para fondos claros: login, tickets) y `logo-dark` (para fondos oscuros: encabezado y panel). Con uno solo, se muestra sobre una placa. Logos de clientes preparados en `assets/clientes/`.

## Multi-tenencia
- **Una base de datos compartida**; cada tabla de un negocio lleva `TenantId`.
- Filtros globales de EF Core (`HasQueryFilter`): ninguna consulta ve datos de otro negocio. El negocio sale del **token** (claim `tid`), nunca del cuerpo de la petición.
- `AppDbContext.SaveChanges` asigna el `TenantId` a lo nuevo y **lanza error** si algo intenta escribir en otro negocio.
- `FindAsync` no se usa: se consulta con `FirstOrDefaultAsync` para que el filtro siempre aplique. El SQL manual (Dapper, `FromSql`) **no** hereda los filtros: hay que filtrar por `TenantId` a mano.
- **Usuarios de plataforma** (rol `Platform`): sin negocio (`TenantId` null), inician sesión **sin** código de negocio, solo llegan a `/api/platform/*`.
- IGV y zona horaria son **por negocio** (`Tenant.IgvRate`, `Tenant.UtcOffsetHours`).
- Sucursales: el cajero pertenece a una; el administrador puede tener una y ver todas. La numeración de tickets es **correlativa por sucursal** (`DocumentCounters`, sin huecos: se asigna dentro de la misma transacción que la venta).

## Endpoints
| Método y ruta | Quién | Notas |
|---|---|---|
| POST /api/auth/login | anónimo | `tenantCode` vacío = plataforma. Bloqueo 15 min tras 5 fallos (423) |
| POST /api/auth/change-password | autenticado | devuelve token nuevo |
| GET/POST/PUT /api/platform/tenants | Platform | alta de negocio = negocio + sucursal + admin (contraseña temporal) |
| GET /api/tenant | negocio | datos del propio negocio |
| GET /api/public/tenants/{code}/branding · /logo · /logo-dark | anónimo | nombre y logos de un negocio activo (para el login) |
| GET /api/tenant/branding · PUT/DELETE /api/tenant/{logo\|logo-dark} | negocio · Admin | PNG/JPG/WebP en base64, máx. 512 KB (SVG no) |
| PUT/DELETE /api/platform/tenants/{id}/{logo\|logo-dark} | Platform | dejar el logo listo al dar de alta |
| GET /api/branches · POST/PUT /api/branches | negocio · Admin | al menos una sucursal activa |
| GET /api/categories, /api/products | negocio | `onlyActive`, `categoryId` |
| POST/PUT/DELETE categorías y productos | Admin | DELETE = desactivar |
| GET /api/menu/today | negocio | menú de la sucursal del usuario (Admin: `?branchId=`) |
| PATCH /api/menu/entries/{id}/sold-out | negocio | cajero: solo hoy y su sucursal |
| GET/PUT /api/menu/{fecha} · POST /api/menu/{fecha}/copy-from/{origen} | Admin | `?branchId=` opcional; copiar no sobrescribe (409) |
| POST /api/sessions/open · GET current · POST {id}/close · GET {id}/summary | negocio | un turno abierto por cajero; la sucursal es la del usuario |
| GET /api/sessions | Admin | historial de cierres (`branchId`, `from`, `to`) |
| POST /api/sales | negocio | idempotente por `Id` (201 nueva, 200 reenvío); `payments` opcional = pago mixto |
| GET /api/tables · POST/PUT /api/tables | negocio · Admin | mesas por sucursal; no se desactiva una mesa con cuenta abierta |
| GET /api/tables/board | negocio | estado de las mesas de la sucursal (Admin: `?branchId=`) |
| POST /api/tables/{id}/order | negocio | abre la cuenta de la mesa (o devuelve la abierta) |
| GET /api/orders/{id} · PUT /api/orders/{id}/lines | negocio | ver / reemplazar los productos de la cuenta |
| POST /api/orders/{id}/move · /cancel | negocio | mover (mesa libre) o unir (mesa ocupada); liberar solo si está vacía |
| POST /api/orders/{id}/checkout | negocio | cobra toda la cuenta o parte (`lines`), con pago simple o mixto; idempotente por `saleId` |
| GET /api/sales | Admin: filtros; Cajero: su turno abierto | paginado (pageSize ≤ 100), `branchId` |
| GET /api/sales/{id} · POST void · POST reprint | Admin o dueño con turno abierto | anular exige motivo |
| GET/POST/PUT /api/users · POST {id}/reset-password | Admin | cajero requiere `branchId` |
| GET /api/reports/summary, /top-products | Admin | `from` inclusivo, `to` exclusivo, `branchId` opcional |

## Reglas de negocio
- Precio y nombre salen del servidor y se copian en cada línea de venta.
- `Product.Price` es SIN IGV. IGV = `Tenant.IgvRate` × subtotal de la venta, redondeado a 2 decimales (AwayFromZero).
- Platos de menú (`IsMenuItem`): solo se venden si están en el menú de la sucursal en la fecha de la venta, al precio de ese día. Si no, 400. "Agotado" es un aviso para la caja; el servidor no rechaza por eso.
- Táper: producto normal ("Táper"); el cliente lo agrega como una línea más al marcar "Para llevar".
- Ventas anuladas: nunca se borran (estado, motivo, usuario, hora + `SaleAudits`).
- Pago mixto: cada venta tiene una o más partes en `SalePayments` que suman exactamente el total (máx. 4). `Sale.PaymentMethod` es el pago principal (el de mayor monto). El cierre de turno y el efectivo esperado se calculan con las partes, así que solo cuenta lo pagado en efectivo.
- Mesas: la cuenta abierta (`TableOrders`) guarda productos y cantidades, no precios. Se muestran con los precios de hoy y al cobrar los fija `SaleService`. El cobro crea la venta y descuenta lo cobrado de la cuenta en la misma transacción. La venta guarda el nombre de la mesa donde se cobró.
- Cajero: anula/reimprime solo ventas propias de su turno abierto. Admin: cualquiera de su negocio.
- Turno cerrado: guarda el esperado al cierre; anular después no lo altera.
- Nunca queda un negocio sin administrador activo ni sin sucursal activa.
- Enums como texto en JSON: los clientes deben registrar `JsonStringEnumConverter`.

## Pendiente / limitaciones conocidas
- Un negocio suspendido (`Active = false`) no puede iniciar sesión, pero los tokens ya emitidos siguen vigentes hasta expirar (8 h).
- Sin límite de intentos por IP en el login (solo bloqueo por usuario): agregar `AddRateLimiter` antes de salir a producción.
- Falta una prueba automática que verifique que un negocio no puede leer ni escribir datos de otro.
- Facturación electrónica (SUNAT) no está implementada: los tickets son comprobantes internos.
