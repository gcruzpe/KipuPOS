namespace KipuPOS.Shared;

public static class Roles
{
    public const string Platform = "Platform"; // dueño/soporte del software: no pertenece a ningún negocio
    public const string Admin = "Admin";
    public const string Cajero = "Cajero";
    public static readonly string[] TenantRoles = [Admin, Cajero];
}

public enum PaymentMethod { Efectivo = 1, Tarjeta = 2, Yape = 3, Plin = 4 }
public enum SaleStatus { Valid = 1, Voided = 2 }

// ---------- Auth ----------
// TenantCode vacío = usuario de plataforma
public record LoginRequest(string? TenantCode, string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Username, string FullName, string Role,
    bool MustChangePassword, string? TenantName, Guid? BranchId);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ---------- Plataforma: negocios (tenants) ----------
public record TenantDto(Guid Id, string Code, string Name, string? Ruc, bool Active, decimal IgvRate, int UtcOffsetHours, DateTime CreatedAtUtc);
public record CreateTenantRequest(string Code, string Name, string? Ruc, string BranchName, string AdminUsername, string AdminFullName);
public record CreateTenantResponse(TenantDto Tenant, BranchDto Branch, UserDto Admin, string TemporaryPassword);
public record UpdateTenantRequest(string Name, string? Ruc, bool Active);

// ---------- Marca del negocio (logos) ----------
public static class LogoKinds
{
    public const string Light = "logo";        // para fondos claros (tinta oscura)
    public const string Dark = "logo-dark";    // para fondos oscuros (tinta clara)
    public static readonly string[] All = [Light, Dark];
}
// Las versiones cambian cada vez que se sube un logo: sirven para armar la URL (?v=) y evitar caché vieja
public record TenantBrandingDto(string Code, string Name, long? LogoVersion, long? LogoDarkVersion);
public record SetLogoRequest(string DataBase64); // PNG, JPG o WebP; máximo 512 KB

// ---------- Sucursales ----------
public record BranchDto(Guid Id, string Name, string? Address, bool Active);
public record BranchUpsert(string Name, string? Address, bool Active = true);

// ---------- Catálogo (Price = precio SIN IGV) ----------
public record CategoryDto(Guid Id, string Name, int SortOrder, bool Active);
public record CategoryUpsert(string Name, int SortOrder, bool Active = true);
public record ProductDto(Guid Id, Guid CategoryId, string Name, decimal Price, bool Active, bool IsMenuItem);
public record ProductUpsert(Guid CategoryId, string Name, decimal Price, bool Active = true, bool IsMenuItem = false);

// ---------- Usuarios (del negocio actual) ----------
public record UserDto(Guid Id, string FullName, string Username, string Role, bool Active, DateTime? LastLoginUtc, Guid? BranchId);
public record CreateUserRequest(string FullName, string Username, string Role, Guid? BranchId);
public record UpdateUserRequest(string FullName, string Role, bool Active, Guid? BranchId);
public record TempPasswordResponse(UserDto User, string TemporaryPassword);

// ---------- Turnos de caja ----------
public record OpenSessionRequest(decimal OpeningCash);
public record CloseSessionRequest(decimal CountedCash, string? Note);
public record CashSessionDto(Guid Id, Guid BranchId, Guid CashierId, string CashierName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc,
    decimal OpeningCash, decimal? CountedCash, decimal? ExpectedCash, decimal? Difference);
public record PaymentTotalDto(PaymentMethod Method, int Count, decimal Total);
public record SessionSummaryDto(CashSessionDto Session, decimal CashSales, decimal ExpectedCash, decimal TotalSales,
    decimal TotalIgv, IReadOnlyList<PaymentTotalDto> ByPayment, int VoidedCount, decimal VoidedTotal);

// ---------- Ventas ----------
public record SaleLineRequest(Guid ProductId, int Quantity, string? Note);
// Pago mixto: varias partes que deben sumar exactamente el total. Sin Payments, se paga todo con PaymentMethod.
public record SalePaymentRequest(PaymentMethod Method, decimal Amount);
public record SalePaymentDto(PaymentMethod Method, decimal Amount);
// Id lo genera el cliente (idempotencia). CreatedAtUtc debe enviarse en UTC.
public record CreateSaleRequest(Guid Id, Guid CashSessionId, DateTime CreatedAtUtc, PaymentMethod PaymentMethod, IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<SalePaymentRequest>? Payments = null);
public record VoidSaleRequest(string Reason);
public record SaleLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, string? Note, decimal LineSubtotal);
// PaymentMethod = el pago principal (el de mayor monto); el detalle está en Payments
public record SaleDto(Guid Id, long Number, DateTime CreatedAtUtc, Guid CashSessionId, Guid CashierId, string CashierName,
    PaymentMethod PaymentMethod, SaleStatus Status, decimal Subtotal, decimal Igv, decimal Total,
    string? VoidReason, string? VoidedByName, DateTime? VoidedAtUtc, int ReprintCount, IReadOnlyList<SaleLineDto> Lines,
    IReadOnlyList<SalePaymentDto> Payments, string? TableName);
public record SaleListItemDto(Guid Id, long Number, DateTime CreatedAtUtc, string CashierName, PaymentMethod PaymentMethod, SaleStatus Status, decimal Total,
    bool MixedPayment = false, string? TableName = null);

// ---------- Mesas y cuentas abiertas ----------
public record DiningTableDto(Guid Id, Guid BranchId, string Name, int SortOrder, bool Active);
public record DiningTableUpsert(Guid BranchId, string Name, int SortOrder, bool Active = true);
// Estado de las mesas para la caja; Subtotal es estimado (sin IGV) con los precios de hoy
public record TableBoardItemDto(Guid TableId, string Name, Guid? OrderId, DateTime? OpenedAtUtc, int Items, decimal Subtotal);
public record OrderLineRequest(Guid ProductId, int Quantity, string? Note);
public record OrderLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, string? Note, decimal LineSubtotal);
public record TableOrderDto(Guid Id, Guid TableId, string TableName, DateTime OpenedAtUtc, string OpenedByName,
    IReadOnlyList<OrderLineDto> Lines, decimal Subtotal);
public record SetOrderLinesRequest(IReadOnlyList<OrderLineRequest> Lines);
public record MoveOrderRequest(Guid TableId); // mesa libre = mover; mesa ocupada = unir
// Lines null = cobrar toda la cuenta; con Lines = dividir (solo esas cantidades)
public record CheckoutOrderRequest(Guid SaleId, Guid CashSessionId, DateTime CreatedAtUtc, PaymentMethod PaymentMethod,
    IReadOnlyList<SalePaymentRequest>? Payments, IReadOnlyList<SaleLineRequest>? Lines);
public record CheckoutResultDto(SaleDto Sale, TableOrderDto? Order); // Order null = la mesa quedó libre
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ---------- Reportes ----------
public record SalesBucketDto(string Label, decimal Total, int Count);
public record SalesSummaryDto(decimal Total, int Count, decimal AverageTicket, int VoidedCount, decimal VoidedTotal, IReadOnlyList<SalesBucketDto> Buckets);
public record TopProductDto(string Name, int Units, decimal Revenue);

// Reporte gerencial: un período y el período anterior de igual duración para comparar.
// Total y montos por forma de pago/sucursal/cajero/canal/tiempo: con IGV. Categorías y productos: sin IGV (lo vendido).
public record KpiDto(decimal Total, decimal Net, decimal Igv, int Tickets, decimal AverageTicket, int VoidedCount, decimal VoidedTotal);
public record LabeledAmountDto(string Label, decimal Total, int Count);
public record ManagementReportDto(DateTime FromUtc, DateTime ToUtc, DateTime PreviousFromUtc, KpiDto Current, KpiDto Previous,
    string TrendUnit, IReadOnlyList<LabeledAmountDto> Trend, IReadOnlyList<LabeledAmountDto> ByHourOfDay, IReadOnlyList<LabeledAmountDto> ByWeekday,
    IReadOnlyList<LabeledAmountDto> ByPayment, IReadOnlyList<LabeledAmountDto> ByChannel, IReadOnlyList<LabeledAmountDto> ByCategory,
    IReadOnlyList<LabeledAmountDto> ByBranch, IReadOnlyList<LabeledAmountDto> ByCashier, IReadOnlyList<TopProductDto> TopProducts);

// ---------- Menú del día (por sucursal; precio sin IGV, por plato y por fecha) ----------
public record MenuEntryDto(Guid Id, Guid ProductId, string Name, decimal Price, bool SoldOut);
public record DailyMenuDto(DateOnly Date, IReadOnlyList<MenuEntryDto> Entries);
public record MenuEntryUpsert(Guid ProductId, decimal Price, bool SoldOut = false);
public record SetMenuRequest(IReadOnlyList<MenuEntryUpsert> Entries);
public record SoldOutRequest(bool SoldOut);
