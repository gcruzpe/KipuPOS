# Hoja de ruta de KipuPOS

Objetivo inmediato: que **Tostadores Café** opere con KipuPOS en su local.
Las fases van en orden: cada una deja algo usable. Marcar `[x]` al terminar cada punto.

## Fase 0 · Base (hecha)
- [x] API multinegocio: negocios, sucursales, catálogo, menú del día, turnos, ventas, usuarios, reportes
- [x] Frontend Blazor con estilo "Andina contemporánea"
- [x] Login, cambio de contraseña, caja, cierre de turno, menú del día (admin), negocios (plataforma)
- [x] Logo por negocio (login, encabezado, panel) y logos de Tostadores Café preparados
- [x] Arreglo del 403 (claim `tid`), repositorio en GitHub, `CLAUDE.md`

## Fase 1 · Tostadores Café puede vender (piloto)
Lo mínimo para un día real de atención. La API ya tiene casi todo; falta sobre todo pantallas.

- [x] **Catálogo (admin):** crear y editar categorías y productos, activar o desactivar
- [x] **Usuarios (admin):** crear cajeros, asignar sucursal, restablecer contraseña
- [x] **Sucursales (admin):** crear y editar
- [x] **Ventas del turno (cajero) / de hoy (admin):** ver tickets, anular con motivo, reimprimir
- [x] **Ticket impreso** con el logo del negocio (58 u 80 mm), marca "COPIA" en reimpresiones
- [x] Arranque de API + Web juntos desde Visual Studio (`KipuPOS.slnLaunch`)
- [x] **Mesas** (6 en Tostadores): cuenta abierta por mesa, cobro al final, pre-cuenta impresa
- [x] Dividir la cuenta, cambiar o unir mesas, **pago mixto** con vuelto (el cierre de turno cuenta cada parte)
- [x] Mesas configurables por sucursal (Admin → Mesas)
- [ ] Probar la impresión real en la **Epson TM-T88V** del local (driver, márgenes, corte)
- [ ] Cargar la **carta real** de Tostadores Café, sus sucursales y usuarios
- [ ] Prueba completa en el equipo del local (tablet o PC + impresora)

Necesito de Tostadores Café: carta con precios · sucursales (con dirección, sale en el ticket) · RUC · nombres de cajeros.
Impresora: Epson TM-T88V (80 mm, 72 mm imprimibles).
Decisión pendiente: ¿la caja muestra precios **con IGV** (como los ve el cliente) o sin IGV?

## Fase 2 · Listo para producción
Para que el sistema viva en internet de forma segura y no solo en una PC.

- [ ] Prueba automática: un negocio no puede leer ni escribir datos de otro
- [ ] Límite de intentos de login por IP (`AddRateLimiter`)
- [ ] Negocio suspendido: invalidar tokens ya emitidos (hoy duran 8 h)
- [ ] Publicación: API + base de datos + frontend en la nube (p. ej. Azure), dominio propio y HTTPS
- [ ] Secretos por variables de entorno, respaldos automáticos de la base, registro de errores
- [ ] Piloto: capacitar al equipo de Tostadores Café y acompañar el primer día

Decisión pendiente: proveedor de hosting y presupuesto mensual.

Más adelante, si hace falta: comanda impresa o pantalla para cocina/barra, rol "Mozo" con usuario propio.

## Fase 3 · Gestión del negocio
Lo que el dueño usa para decidir, una vez que la caja funciona a diario.

- [x] Reportes gerenciales (Admin → Reportes): ventas, tickets y ticket promedio contra el período anterior; tendencia; horas pico; día de la semana; formas de pago; mesas vs mostrador; categorías; top 10; sucursales; cajeros
- [ ] Exportar reportes a Excel/PDF y envío automático del resumen diario al dueño
- [ ] Historial de turnos y diferencias de caja
- [ ] Historial de ventas con filtros (admin)
- [ ] Datos del negocio y logo desde el panel de plataforma
- [ ] App instalable en tablet (PWA) y tolerancia a cortes breves de internet

## Fase 4 · Formalización y crecimiento
- [ ] Facturación electrónica SUNAT (boletas y facturas mediante un OSE/PSE)
- [ ] Planes y precios de suscripción para nuevos negocios
- [ ] Alta de nuevos clientes guiada y página de presentación de KipuPOS
