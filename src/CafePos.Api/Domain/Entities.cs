using System.ComponentModel.DataAnnotations;
using CafePos.Shared;

namespace CafePos.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Active { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
}

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; } // sin IGV
    public bool Active { get; set; } = true;
}

public class CashSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CashierId { get; set; }
    public User? Cashier { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? Difference { get; set; }
    public string? Note { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public class Sale
{
    public Guid Id { get; set; } // generado por el cliente
    public long Number { get; set; } // secuencia de SQL Server
    public DateTime CreatedAtUtc { get; set; }  // cuando se vendió (reloj del cliente)
    public DateTime ReceivedAtUtc { get; set; } // cuando llegó al servidor (útil en modo offline)
    public Guid CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }
    public Guid CashierId { get; set; }
    public User? Cashier { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Valid;
    public string? VoidReason { get; set; }
    public Guid? VoidedById { get; set; }
    public User? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public int Position { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = ""; // snapshot
    public decimal UnitPrice { get; set; }        // snapshot, sin IGV
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public decimal LineSubtotal { get; set; }
}

public enum SaleAuditType { Reprint = 1, Void = 2 }

public class SaleAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public SaleAuditType Type { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime AtUtc { get; set; }
    public string? Detail { get; set; }
}
