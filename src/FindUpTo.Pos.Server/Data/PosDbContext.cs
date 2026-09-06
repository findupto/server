using FindUpTo.Pos.Server.Endpoints;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Data;

public class PosDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BusinessSetting> BusinessSettings => Set<BusinessSetting>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PosOrder> Orders => Set<PosOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<AppUser>().Property(x => x.Username).HasMaxLength(64);
        modelBuilder.Entity<AppUser>().Property(x => x.Role).HasMaxLength(32);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.BusinessName).HasMaxLength(160);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencyCode).HasMaxLength(8);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencySymbol).HasMaxLength(16);
        modelBuilder.Entity<Category>().Property(x => x.Name).HasMaxLength(120);
        modelBuilder.Entity<Product>().Property(x => x.Name).HasMaxLength(160);
        modelBuilder.Entity<Customer>().Property(x => x.Phone).HasMaxLength(32);
        modelBuilder.Entity<PosOrder>().Property(x => x.OrderType).HasMaxLength(32);
        modelBuilder.Entity<PosOrder>().Property(x => x.Status).HasMaxLength(32);
        modelBuilder.Entity<OrderItem>().Property(x => x.ProductName).HasMaxLength(160);
        modelBuilder.Entity<OrderItem>().HasOne<PosOrder>().WithMany(x => x.Items).HasForeignKey(x => x.PosOrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Payment>().Property(x => x.Method).HasMaxLength(32);
        modelBuilder.Entity<Payment>().Property(x => x.Status).HasMaxLength(32);
        modelBuilder.Entity<Promotion>().Property(x => x.Name).HasMaxLength(160);
        modelBuilder.Entity<Promotion>().Property(x => x.DiscountType).HasMaxLength(16);
        modelBuilder.Entity<Conversation>().Property(x => x.Participants).HasMaxLength(2000);
        modelBuilder.Entity<Message>().Property(x => x.SenderUsername).HasMaxLength(64);
        modelBuilder.Entity<Message>().Property(x => x.Text).HasMaxLength(4000);
        modelBuilder.Entity<AuditLog>().Property(x => x.Username).HasMaxLength(64);
        modelBuilder.Entity<AuditLog>().Property(x => x.Action).HasMaxLength(64);
        modelBuilder.Entity<AuditLog>().Property(x => x.EntityType).HasMaxLength(64);
        modelBuilder.Entity<AuditLog>().Property(x => x.EntityId).HasMaxLength(64);
        modelBuilder.Entity<AuditLog>().Property(x => x.Details).HasMaxLength(4000);
    }
}

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : PosDbContext(options);