using System.Globalization;
using KipuPOS.Shared;

namespace KipuPOS.Web.Services;

public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Formato peruano: S/ 1,234.50
    public static string Money(decimal v) => (v < 0 ? "− S/ " : "S/ ") + Math.Abs(v).ToString("N2", Inv);
    public static string Ticket(long number) => "#" + number.ToString("D6", Inv);
    public static string Time(DateTime local) => local.ToString("HH:mm", Inv);
    public static string Stamp(DateTime local) => local.ToString("dd/MM/yyyy HH:mm", Inv);
    public static string Role(string role) => role == Roles.Cajero ? "Cajero" : role == Roles.Admin ? "Administrador" : role;

    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // Acepta "12.5" o "12,5"
    public static bool TryParseMoney(string? text, out decimal value) =>
        decimal.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Number, Inv, out value);

    public static string Method(PaymentMethod m) => m.ToString();

    private static readonly string[] Days = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];
    private static readonly string[] Months = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
    public static string LongDate(DateOnly d) => $"{Days[(int)d.DayOfWeek]} {d.Day} de {Months[d.Month - 1]}";
}
