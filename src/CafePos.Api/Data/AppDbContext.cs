using CafePos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleAudit> SaleAudits => Set<SaleAudit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>("SaleNumber").StartsAt(1).IncrementsBy(1);

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(30);
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(20);
        });

        b.Entity<Category>(e => e.Property(x => x.Name).HasMaxLength(80));

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CashSession>(e =>
        {
            e.Property(x => x.OpeningCash).HasPrecision(18, 2);
            e.Property(x => x.CountedCash).HasPrecision(18, 2);
            e.Property(x => x.ExpectedCash).HasPrecision(18, 2);
            e.Property(x => x.Difference).HasPrecision(18, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Un solo turno abierto por cajero
            e.HasIndex(x => x.CashierId).IsUnique().HasFilter("[ClosedAtUtc] IS NULL");
            e.HasIndex(x => x.OpenedAtUtc);
            e.HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Sale>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasDefaultValueSql("NEXT VALUE FOR SaleNumber");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CashSessionId);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.Igv).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.VoidReason).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.CashSession).WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VoidedBy).WithMany().HasForeignKey(x => x.VoidedById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SaleLine>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(120);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.HasIndex(x => x.ProductId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SaleAudit>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(200);
            e.HasIndex(x => new { x.SaleId, x.Type });
            e.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
