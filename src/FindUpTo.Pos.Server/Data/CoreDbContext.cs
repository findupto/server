using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Data;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BusinessSetting> BusinessSettings => Set<BusinessSetting>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PosOrder> Orders => Set<PosOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<AppUser>().Property(x => x.Username).HasMaxLength(64);
        modelBuilder.Entity<AppUser>().Property(x => x.Role).HasMaxLength(32);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.BusinessName).HasMaxLength(160);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencyCode).HasMaxLength(8);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencySymbol).HasMaxLength(16);
        modelBuilder.Entity<Category>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<Product>().Property(x => x.Name).HasMaxLength(160);
        modelBuilder.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        modelBuilder.Entity<Customer>().Property(x => x.Phone).HasMaxLength(32);
        modelBuilder.Entity<PosOrder>().Property(x => x.Status).HasMaxLength(32);
        modelBuilder.Entity<PosOrder>().Property(x => x.OrderType).HasMaxLength(32);
        modelBuilder.Entity<PosOrder>().Property(x => x.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<PosOrder>().Property(x => x.Tax).HasPrecision(18, 2);
        modelBuilder.Entity<PosOrder>().Property(x => x.Total).HasPrecision(18, 2);
        modelBuilder.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<OrderItem>().Property(x => x.LineTotal).HasPrecision(18, 2);
        modelBuilder.Entity<PosOrder>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PosOrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Product>().HasIndex(x => x.Name);
        modelBuilder.Entity<PosOrder>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
