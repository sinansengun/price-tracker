using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Models;

namespace PriceTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<UserProduct> UserProducts => Set<UserProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tablolarını oluştur

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Url).IsUnique();
            e.Property(p => p.CurrentPrice).HasColumnType("decimal(18,2)");
            e.Property(p => p.InitialPrice).HasColumnType("decimal(18,2)");
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

        modelBuilder.Entity<UserProduct>(e =>
        {
            e.HasKey(up => up.Id);
            e.Property(up => up.TargetPrice).HasColumnType("decimal(18,2)");
            e.HasIndex(up => new { up.UserId, up.ProductId }).IsUnique();
            e.HasOne(up => up.User)
             .WithMany(u => u.UserProducts)
             .HasForeignKey(up => up.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(up => up.Product)
             .WithMany(p => p.UserProducts)
             .HasForeignKey(up => up.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Label>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasOne(l => l.User)
             .WithMany(u => u.Labels)
             .HasForeignKey(l => l.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserProduct>()
            .HasMany(up => up.Labels)
            .WithMany(l => l.UserProducts)
            .UsingEntity("UserProductLabel");
    }
}
