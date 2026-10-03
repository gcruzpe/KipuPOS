namespace CafePos.Shared;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Cajero = "Cajero";
    public static readonly string[] All = [Admin, Cajero];
}

public enum PaymentMethod { Efectivo = 1, Tarjeta = 2, Yape = 3, Plin = 4 }
public enum SaleStatus { Valid = 1, Voided = 2 }

// ---------- Auth ----------
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Username, string FullName, string Role, bool MustChangePassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ---------- Catálogo (Price = precio SIN IGV) ----------
public record CategoryDto(Guid Id, string Name, int SortOrder, bool Active);
public record CategoryUpsert(string Name, int SortOrder, bool Active = true);
public record ProductDto(Guid Id, Guid CategoryId, string Name, decimal Price, bool Active);
public record ProductUpsert(Guid CategoryId, string Name, decimal Price, bool Active = true);

// ---------- Usuarios ----------
public record UserDto(Guid Id, string FullName, string Username, string Role, bool Active, DateTime? LastLoginUtc);
public record CreateUserRequest(string FullName, string Username, string Role);
public record UpdateUserRequest(string FullName, string Role, bool Active);
public record TempPasswordResponse(UserDto User, string TemporaryPassword);

// ---------- Turnos de caja ----------
public record OpenSessionRequest(decimal OpeningCash);
public record CloseSessionRequest(decimal CountedCash, string? Note);
public record CashSessionDto(Guid Id, Guid CashierId, string CashierName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc,
    decimal OpeningCash, decimal? CountedCash, decimal? ExpectedCash, decimal? Difference);
public record PaymentTotalDto(PaymentMethod Method, int Count, decimal Total);
public record SessionSummaryDto(CashSessionDto Session, decimal CashSales, decimal ExpectedCash, decimal TotalSales,
    decimal TotalIgv, IReadOnlyList<PaymentTotalDto> ByPayment, int VoidedCount, decimal VoidedTotal);

// ---------- Ventas ----------
public record SaleLineRequest(Guid ProductId, int Quantity, string? Note);
// Id lo genera el cliente (idempotencia). CreatedAtUtc debe enviarse en UTC.
public record CreateSaleRequest(Guid Id, Guid CashSessionId, DateTime CreatedAtUtc, PaymentMethod PaymentMethod, IReadOnlyList<SaleLineRequest> Lines);
public record VoidSaleRequest(string Reason);
public record SaleLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, string? Note, decimal LineSubtotal);
public record SaleDto(Guid Id, long Number, DateTime CreatedAtUtc, Guid CashSessionId, Guid CashierId, string CashierName,
    PaymentMethod PaymentMethod, SaleStatus Status, decimal Subtotal, decimal Igv, decimal Total,
    string? VoidReason, string? VoidedByName, DateTime? VoidedAtUtc, int ReprintCount, IReadOnlyList<SaleLineDto> Lines);
public record SaleListItemDto(Guid Id, long Number, DateTime CreatedAtUtc, string CashierName, PaymentMethod PaymentMethod, SaleStatus Status, decimal Total);
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ---------- Reportes ----------
public record SalesBucketDto(string Label, decimal Total, int Count);
public record SalesSummaryDto(decimal Total, int Count, decimal AverageTicket, int VoidedCount, decimal VoidedTotal, IReadOnlyList<SalesBucketDto> Buckets);
public record TopProductDto(string Name, int Units, decimal Revenue);
