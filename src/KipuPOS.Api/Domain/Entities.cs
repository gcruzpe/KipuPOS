using System.ComponentModel.DataAnnotations;
using KipuPOS.Shared;

namespace KipuPOS.Api.Domain;

// Toda entidad que pertenece a un negocio lleva TenantId.
// AppDbContext lo asigna al guardar y lo filtra automáticamente en cada consulta.
public interface ITenantEntity { Guid TenantId { get; set; } }

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";   // código de acceso (único); se escribe en el login
    public string Name { get; set; } = "";
    public string? Ruc { get; set; }
    public bool Active { get; set; } = true;
    public decimal IgvRate { get; set; } = 0.18m;
    public int UtcOffsetHours { get; set; } = -5; // Lima, sin horario de verano
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Archivos de marca del negocio (logos). Tabla aparte para no cargar binarios cada vez que se lee el negocio.
// No es ITenantEntity: el login la consulta sin token, así que cada endpoint la acota por TenantId a mano.
public class TenantAsset
{
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = "";        // LogoKinds
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Branch : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public bool Active { get; set; } = true;
}

// Contador de documentos por negocio, sucursal y tipo (numeración correlativa sin huecos)
public class DocumentCounter : ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Type { get; set; } = "";
    public long LastNumber { get; set; }
}

public class User // no implementa ITenantEntity: los usuarios de plataforma (rol Platform) no tienen negocio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; } // null solo para el rol Platform
    public Guid? BranchId { get; set; } // obligatorio para cajeros; opcional para administradores
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

public class Category : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public List<Product> Products { get; set; } = [];
}

public class Product : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; } // sin IGV (en platos de menú es el precio habitual; el del día va en DailyMenuEntry)
    public bool Active { get; set; } = true;
    public bool IsMenuItem { get; set; } // plato de menú: se vende solo si está en el menú del día
}

public class DailyMenuEntry : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; } // el menú es por sucursal
    public DateOnly Date { get; set; } // fecha de negocio (hora local del negocio)
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Price { get; set; } // sin IGV, precio de ese día
    public bool SoldOut { get; set; }  // aviso para la caja; no bloquea ventas en el servidor
    public int SortOrder { get; set; }
}

public class CashSession : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
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

public class Sale : ITenantEntity
{
    public Guid Id { get; set; } // generado por el cliente
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public long Number { get; set; } // correlativo por sucursal (DocumentCounter)
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
    public Guid? TableOrderId { get; set; } // cuenta de mesa de la que salió (null en ventas de mostrador)
    public TableOrder? TableOrder { get; set; }
    public string? TableName { get; set; }  // snapshot: la mesa donde se cobró (la cuenta puede moverse después)
    public List<SaleLine> Lines { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

// Partes del pago de una venta (pago mixto). Siempre hay al menos una y suman Sale.Total.
public class SalePayment : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}

public class DiningTable : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}

public enum TableOrderStatus { Open = 1, Closed = 2, Cancelled = 3 }

// Cuenta abierta de una mesa: se van agregando productos y se cobra al final (en una o varias ventas)
public class TableOrder : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid TableId { get; set; }
    public DiningTable? Table { get; set; }
    public Guid OpenedById { get; set; }
    public User? OpenedBy { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public TableOrderStatus Status { get; set; } = TableOrderStatus.Open;
    public List<TableOrderLine> Lines { get; set; } = [];
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

// El precio no se guarda aquí: se toma del servidor al cobrar (igual que en una venta normal)
public class TableOrderLine : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public int Position { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}

public class SaleLine : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
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

public class SaleAudit : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public SaleAuditType Type { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime AtUtc { get; set; }
    public string? Detail { get; set; }
}
