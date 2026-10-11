using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using KipuPOS.Shared;
using Microsoft.EntityFrameworkCore;

namespace KipuPOS.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _tenant = tenant;

    // Negocio del usuario autenticado. Sin token (login, arranque) o con un token de plataforma es null y los filtros
    // no devuelven nada: quien necesite consultar fuera de un negocio debe usar IgnoreQueryFilters() a propósito.
    private Guid? CurrentTenantId => _tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantAsset> TenantAssets => Set<TenantAsset>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<DailyMenuEntry> DailyMenuEntries => Set<DailyMenuEntry>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleAudit> SaleAudits => Set<SaleAudit>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<TableOrder> TableOrders => Set<TableOrder>();
    public DbSet<TableOrderLine> TableOrderLines => Set<TableOrderLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Ruc).HasMaxLength(11);
            e.HasIndex(x => x.Ruc).IsUnique().HasFilter("[Ruc] IS NOT NULL");
            e.Property(x => x.IgvRate).HasPrecision(5, 4);
        });

        b.Entity<TenantAsset>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.Kind });
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Branch>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Address).HasMaxLength(200);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<DocumentCounter>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.BranchId, x.Type });
            e.Property(x => x.Type).HasMaxLength(20);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        // Los usuarios de plataforma (rol Platform) no tienen negocio: TenantId es null y solo se ven con IgnoreQueryFilters()
        b.Entity<User>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Username }).IsUnique();
            e.Property(x => x.Username).HasMaxLength(30);
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        // Menú del día por sucursal: un plato, un precio, por fecha y sucursal
        b.Entity<DailyMenuEntry>(e =>
        {
            e.HasIndex(x => new { x.BranchId, x.Date, x.ProductId }).IsUnique();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<CashSession>(e =>
        {
            e.Property(x => x.OpeningCash).HasPrecision(18, 2);
            e.Property(x => x.CountedCash).HasPrecision(18, 2);
            e.Property(x => x.ExpectedCash).HasPrecision(18, 2);
            e.Property(x => x.Difference).HasPrecision(18, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Un solo turno abierto por cajero (un cajero pertenece a un solo negocio)
            e.HasIndex(x => x.CashierId).IsUnique().HasFilter("[ClosedAtUtc] IS NULL");
            e.HasIndex(x => new { x.TenantId, x.OpenedAtUtc });
            e.HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Sale>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).ValueGeneratedNever(); // lo asigna DocumentNumberService dentro de la transacción
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.Number }).IsUnique();
            e.HasIndex(x => x.CashSessionId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.Igv).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.VoidReason).HasMaxLength(200);
            e.Property(x => x.TableName).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.CashSession).WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VoidedBy).WithMany().HasForeignKey(x => x.VoidedById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TableOrder).WithMany().HasForeignKey(x => x.TableOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SalePayment>(e =>
        {
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.SaleId, x.Method });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<DiningTable>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(40);
            e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<TableOrder>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Una sola cuenta abierta por mesa
            e.HasIndex(x => x.TableId).IsUnique().HasFilter("[Status] = 'Open'");
            e.HasIndex(x => new { x.BranchId, x.Status });
            e.HasOne(x => x.Table).WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OpenedBy).WithMany().HasForeignKey(x => x.OpenedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<TableOrderLine>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SaleLine>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(120);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.HasIndex(x => x.ProductId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SaleAudit>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(200);
            e.HasIndex(x => new { x.SaleId, x.Type });
            e.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        // Todas las demás entidades de un negocio apuntan a Tenant
        foreach (var t in b.Model.GetEntityTypes().Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)).ToList())
        {
            b.Entity(t.ClrType).HasOne(typeof(Tenant)).WithMany()
                .HasForeignKey(nameof(ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Asigna el negocio a lo nuevo y bloquea cualquier escritura que cruce de negocio.
    // Con un contexto sin negocio (arranque, plataforma) se aceptan TenantId explícitos.
    private void ApplyTenant()
    {
        var current = _tenant.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            var entity = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entity.TenantId == Guid.Empty)
                        entity.TenantId = current ?? throw new InvalidOperationException("No hay negocio en contexto para guardar este registro.");
                    else if (current is not null && entity.TenantId != current)
                        throw new InvalidOperationException("No se pueden escribir datos de otro negocio.");
                    break;
                case EntityState.Modified or EntityState.Deleted:
                    if (current is not null && entity.TenantId != current)
                        throw new InvalidOperationException("No se pueden modificar datos de otro negocio.");
                    break;
            }
        }

        // Usuarios: TenantId opcional (null solo para el rol Platform)
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            var u = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                if (u.Role == Roles.Platform)
                {
                    if (u.TenantId is not null)
                        throw new InvalidOperationException("Un usuario de plataforma no pertenece a ningún negocio.");
                }
                else if (u.TenantId is null)
                    u.TenantId = current ?? throw new InvalidOperationException("No hay negocio en contexto para crear este usuario.");
                else if (current is not null && u.TenantId != current)
                    throw new InvalidOperationException("No se pueden escribir datos de otro negocio.");
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (current is not null && u.TenantId != current)
                    throw new InvalidOperationException("No se pueden modificar datos de otro negocio.");
            }
        }
    }
}
