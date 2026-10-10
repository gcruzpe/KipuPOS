using System.Security.Claims;
using KipuPOS.Api.Domain;
using KipuPOS.Shared;

namespace KipuPOS.Api.Support;

// ITenantContext, HttpTenantContext y TenantSettings viven en Tenancy.cs.
public static class AppClaims
{
    // Mismos nombres que lee HttpTenantContext ("tid" = negocio, "bid" = sucursal)
    public const string Tenant = "tid";
    public const string Branch = "bid";
    public const string MustChangePassword = "mcp";
}

public static class DocumentTypes
{
    public const string Ticket = "Ticket";
}

public static class Money
{
    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

public static class Text
{
    public static string? Trunc(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal u) =>
        Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier) ?? u.FindFirstValue("sub")
                   ?? throw new InvalidOperationException("Token sin identificador de usuario."));
    public static bool IsAdmin(this ClaimsPrincipal u) => u.IsInRole(Roles.Admin);
}

public record ServiceResult<T>(T? Value, int Status, string? Error)
{
    public bool Ok => Status is >= 200 and < 300;
    public static ServiceResult<T> Success(T value, int status = 200) => new(value, status, null);
    public static ServiceResult<T> Fail(int status, string error) => new(default, status, error);
    public IResult ToHttp() => Ok ? Results.Json(Value, statusCode: Status) : Results.Problem(detail: Error, statusCode: Status);
}

public static class Mapping
{
    public static SaleDto ToDto(this Sale s, int reprints) => new(
        s.Id, s.Number, s.CreatedAtUtc, s.CashSessionId, s.CashierId, s.Cashier?.FullName ?? "",
        s.PaymentMethod, s.Status, s.Subtotal, s.Igv, s.Total, s.VoidReason, s.VoidedBy?.FullName, s.VoidedAtUtc, reprints,
        s.Lines.OrderBy(l => l.Position)
            .Select(l => new SaleLineDto(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.Note, l.LineSubtotal)).ToList());

    public static UserDto ToDto(this User u) => new(u.Id, u.FullName, u.Username, u.Role, u.Active, u.LastLoginUtc, u.BranchId);
    public static TenantDto ToDto(this Tenant t) => new(t.Id, t.Code, t.Name, t.Ruc, t.Active, t.IgvRate, t.UtcOffsetHours, t.CreatedAtUtc);
    public static BranchDto ToDto(this Branch b) => new(b.Id, b.Name, b.Address, b.Active);
}
