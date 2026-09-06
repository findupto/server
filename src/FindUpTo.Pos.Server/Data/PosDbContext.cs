using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Data;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BusinessSetting> BusinessSettings => Set<BusinessSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<AppUser>().Property(x => x.Username).HasMaxLength(64);
        modelBuilder.Entity<AppUser>().Property(x => x.Role).HasMaxLength(32);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.BusinessName).HasMaxLength(160);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencyCode).HasMaxLength(8);
        modelBuilder.Entity<BusinessSetting>().Property(x => x.CurrencySymbol).HasMaxLength(16);
    }
}
