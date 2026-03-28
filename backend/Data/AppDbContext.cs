using Microsoft.EntityFrameworkCore;
using PriceTracker.Models;

namespace PriceTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.CurrentPrice).HasColumnType("decimal(18,2)");
            e.Property(p => p.TargetPrice).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<PriceHistory>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Price).HasColumnType("decimal(18,2)");
            e.HasOne(h => h.Product)
             .WithMany(p => p.PriceHistories)
             .HasForeignKey(h => h.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>()
            .HasMany(p => p.Labels)
            .WithMany(l => l.Products)
            .UsingEntity("ProductLabel");
    }
}
