using Microsoft.EntityFrameworkCore;
using FyreApp.Models;

namespace FyreApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<MaintenanceSchedule> MaintenanceSchedules { get; set; }
    public DbSet<MaintenanceInterval> MaintenanceIntervals { get; set; }
    public DbSet<MaintenanceHistory> MaintenanceHistory => Set<MaintenanceHistory>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Client → Sites (1:N)
        modelBuilder.Entity<Client>()
            .HasMany(c => c.Sites)
            .WithOne(s => s.Client)
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Site → Assets (1:N)
        modelBuilder.Entity<Site>()
            .HasMany(s => s.Assets)
            .WithOne(a => a.Site)
            .HasForeignKey(a => a.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Asset ↔ AssetType (N:N)
        modelBuilder.Entity<Asset>()
            .HasMany(a => a.AssetTypes)
            .WithMany(t => t.Assets)
            .UsingEntity(j => j.ToTable("AssetAssetTypes"));

        modelBuilder.Entity<MaintenanceSchedule>()
            .HasOne(ms => ms.Asset)
            .WithMany(a => a.MaintenanceSchedules)
            .HasForeignKey(ms => ms.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaintenanceSchedule>()
            .HasOne(ms => ms.Site)
            .WithMany(s => s.MaintenanceSchedules)
            .HasForeignKey(ms => ms.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaintenanceSchedule>()
            .HasOne(mi => mi.MaintenanceInterval)
            .WithMany(s => s.MaintenanceSchedules)
            .HasForeignKey(mi => mi.MaintenanceIntervalId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MaintenanceHistory>(entity =>
        {
            entity.HasOne(h => h.MaintenanceSchedule)
                .WithMany(s => s.MaintenanceHistory)
                .HasForeignKey(h => h.MaintenanceScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(h => h.MaintenanceScheduleId);
            entity.HasIndex(h => h.CompletedAt);
        });

    }

}
