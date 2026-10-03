# CafePos – API (v2: modelo completo del diseño)

## Puesta en marcha
```
dotnet new sln -n CafePos
dotnet sln add src/CafePos.Shared src/CafePos.Api
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial -p src/CafePos.Api      # si ya existía una "Initial" de la v1, bórrala antes
dotnet run --project src/CafePos.Api
```
Primer ingreso: usuario `Seed:AdminUser`, contraseña `Seed:AdminPassword`. El login responde `mustChangePassword: true`
y con ese token solo funciona `POST /api/auth/change-password` (devuelve un token nuevo).
En producción: `Jwt__Key`, `ConnectionStrings__Default`, `Seed__AdminPassword`, `Cors__Origins__0` por variables de entorno.

## Endpoints
| Método y ruta | Quién | Notas |
|---|---|---|
| POST /api/auth/login | anónimo | bloqueo 15 min tras 5 fallos (423) |
| POST /api/auth/change-password | autenticado | devuelve token nuevo |
| GET /api/categories, /api/products | autenticado | `onlyActive`, `categoryId` |
| POST/PUT/DELETE categorías y productos | Admin | DELETE = desactivar |
| POST /api/sessions/open | autenticado | un turno abierto por cajero (409 si ya hay) |
| GET /api/sessions/current | autenticado | 204 si no hay turno abierto |
| POST /api/sessions/{id}/close | dueño o Admin | guarda contado, esperado y diferencia |
| GET /api/sessions/{id}/summary | dueño o Admin | totales por método, IGV, anuladas |
| GET /api/sessions | Admin | historial de cierres (from, to, page) |
| POST /api/sales | autenticado | idempotente por `Id`; 201 nueva, 200 reenvío |
| GET /api/sales | Admin: filtros; Cajero: solo su turno abierto | paginado (pageSize ≤ 100) |
| GET /api/sales/{id} | Admin o dueño con turno abierto | líneas + auditoría |
| POST /api/sales/{id}/void | Admin o dueño con turno abierto | motivo obligatorio |
| POST /api/sales/{id}/reprint | Admin o dueño con turno abierto | registra auditoría; cliente imprime "COPIA" |
| GET/POST/PUT /api/users, POST /api/users/{id}/reset-password | Admin | contraseña temporal, se muestra una vez |
| GET /api/reports/summary, /top-products | Admin | `from` inclusivo, `to` exclusivo |

## Reglas de negocio implementadas
- Precio y nombre salen del servidor (nunca del cliente) y se copian en cada línea.
- `Product.Price` es SIN IGV. IGV = `Tax:IgvRate` × subtotal de la venta, redondeado a 2 decimales (AwayFromZero).
- Las ventas anuladas nunca se borran: estado, motivo, usuario y hora, más un registro en `SaleAudits`.
- Cajero: anula/reimprime solo ventas propias de su turno abierto. Admin: cualquiera.
- Turno cerrado: guarda el esperado al cierre; anular después no lo altera.
- Nunca queda el sistema sin un administrador activo; nadie puede desactivarse a sí mismo.
- Enums como texto en JSON: los clientes deben registrar `JsonStringEnumConverter`.
