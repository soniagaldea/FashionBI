using FashionDataAnalysisPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace FashionDataAnalysisPlatform.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<StoreConnection> StoreConnections => Set<StoreConnection>();
        public DbSet<Store> Stores => Set<Store>();

        public DbSet<ForecastResult> ForecastResults => Set<ForecastResult>();
        public DbSet<ForecastAccuracy> ForecastAccuracies => Set<ForecastAccuracy>();
        public DbSet<ForecastFeatureImportance> ForecastFeatureImportances => Set<ForecastFeatureImportance>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .HasIndex(p => new { p.StoreConnectionId, p.StoreId, p.ProductCode })
                .IsUnique();

            modelBuilder.Entity<Sale>()
                .HasOne(s => s.Product)
                .WithMany(p => p.Sales)
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.Product)
                .WithMany(p => p.Inventories)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Sale>()
                .HasOne(s => s.StoreConnection)
                .WithMany()
                .HasForeignKey(s => s.StoreConnectionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Store>()
                .HasOne(s => s.StoreConnection)
                .WithMany()
                .HasForeignKey(s => s.StoreConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Product>()
                .HasOne(p => p.Store)
                .WithMany()
                .HasForeignKey(p => p.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Product>()
                .HasOne(p => p.StoreConnection)
                .WithMany()
                .HasForeignKey(p => p.StoreConnectionId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.Store)
                .WithMany()
                .HasForeignKey(i => i.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.StoreConnection)
                .WithMany()
                .HasForeignKey(i => i.StoreConnectionId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Sale>()
                .HasOne(s => s.Store)
                .WithMany()
                .HasForeignKey(s => s.StoreId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}