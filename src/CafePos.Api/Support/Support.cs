using System.Security.Claims;
using CafePos.Api.Domain;
using CafePos.Shared;

namespace CafePos.Api.Support;

public class TaxOptions { public decimal IgvRate { get; set; } = 0.18m; }
public class BusinessOptions { public int UtcOffsetHours { get; set; } = -5; } // Lima (sin horario de verano)

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

    public static UserDto ToDto(this User u) => new(u.Id, u.FullName, u.Username, u.Role, u.Active, u.LastLoginUtc);
}
