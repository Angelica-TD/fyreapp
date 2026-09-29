using FyreApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
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
    public DbSet<ClientTask> ClientTasks => Set<ClientTask>();
    public DbSet<AssetCatalogue> AssetCatalogue => Set<AssetCatalogue>();
    public DbSet<ServiceQuote> ServiceQuotes => Set<ServiceQuote>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLineItem> QuoteLineItems => Set<QuoteLineItem>();



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Client → Sites (1:N)
        modelBuilder.Entity<Client>(entity =>
        {
            entity.Property(c => c.Name)
                    .IsRequired()
                    .HasMaxLength(200);
            
            entity.HasIndex(c => c.Name).IsUnique();

            entity.Property(c => c.ExternalId)
                    .HasMaxLength(64);

            entity.HasIndex(c => c.ExternalId).IsUnique();

            // Postgres-friendly UTC timestamps
            entity.Property(x => x.Created)
                    .HasColumnType("timestamptz")
                    .HasDefaultValueSql("now()")
                    .ValueGeneratedOnAdd();

            entity.Property(x => x.Updated)
                  .HasColumnType("timestamptz");

            entity.Property(x => x.PrimaryContactName).HasMaxLength(200);
            entity.Property(x => x.PrimaryContactEmail).HasMaxLength(320);
            entity.Property(x => x.PrimaryContactMobile).HasMaxLength(32);
            entity.Property(x => x.PrimaryContactCcEmail).HasMaxLength(320);

            entity.Property(x => x.PrimaryContactAddress).HasMaxLength(320);
            // entity.Property(x => x.PrimaryStreetAddress).HasMaxLength(200);
            // entity.Property(x => x.PrimarySuburb).HasMaxLength(100);
            // entity.Property(x => x.PrimaryState).HasMaxLength(10);
            // entity.Property(x => x.PrimaryPostcode).HasMaxLength(16);

            entity.Property(x => x.BillingName).HasMaxLength(200);
            entity.Property(x => x.BillingAttentionTo).HasMaxLength(200);
            entity.Property(x => x.BillingEmail).HasMaxLength(320);
            entity.Property(x => x.BillingCcEmail).HasMaxLength(320);
            entity.Property(x => x.BillingAddress).HasMaxLength(320);
            // entity.Property(x => x.BillingSuburb).HasMaxLength(100);
            // entity.Property(x => x.BillingState).HasMaxLength(10);
            // entity.Property(x => x.BillingPostcode).HasMaxLength(16);

            entity.Property(x => x.Active).HasDefaultValue(true);

            // searching by these fields is common
            entity.HasIndex(x => x.Active);
            entity.HasIndex(x => x.PrimaryContactEmail);
            entity.HasIndex(x => x.BillingEmail);
        });

        // Site → Assets (1:N)
        modelBuilder.Entity<Site>()
            .HasMany(s => s.Assets)
            .WithOne(a => a.Site)
            .HasForeignKey(a => a.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Site>()
            .Property(s => s.Active)
            .HasDefaultValue(true);

        modelBuilder.Entity<Site>()
            .HasIndex(s => s.ExternalId)
            .IsUnique();

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

        modelBuilder.Entity<ClientTask>(entity =>
        {
            entity.HasOne(t => t.Client)
                .WithMany()
                .HasForeignKey(t => t.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.Site)
                .WithMany()
                .HasForeignKey(t => t.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => t.ClientId);
            entity.HasIndex(t => t.SiteId);
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.DueDateUtc);
        });

        modelBuilder.Entity<ClientTask>()
            .HasOne(t => t.MaintenanceSchedule)
            .WithMany(s => s.GeneratedTasks)
            .HasForeignKey(t => t.MaintenanceScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ClientTask>()
            .HasOne(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<AssetCatalogue>()
            .HasIndex(a => a.Name)
            .IsUnique();

        modelBuilder.Entity<MaintenanceInterval>()
            .HasIndex(i => i.Name)
            .IsUnique();

        modelBuilder.Entity<ServiceOffering>(entity =>
        {
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.Description).HasMaxLength(1000);
            entity.Property(s => s.IsActive).HasDefaultValue(true);

            entity.HasMany(s => s.Intervals)
                .WithMany(i => i.ServiceOfferings)
                .UsingEntity(j => j.ToTable("ServiceOfferingIntervals"));
        });

        modelBuilder.Entity<ServiceQuote>(entity =>
        {
            entity.HasOne(q => q.Client)
                .WithMany()
                .HasForeignKey(q => q.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(q => q.Site)
                .WithMany()
                .HasForeignKey(q => q.SiteId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.HasIndex(q => q.SiteId);

            entity.HasOne(q => q.ServiceOffering)
                .WithMany()
                .HasForeignKey(q => q.ServiceOfferingId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasIndex(q => q.ServiceOfferingId);

            entity.HasOne(q => q.MaintenanceInterval)
                .WithMany()
                .HasForeignKey(q => q.MaintenanceIntervalId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.Property(q => q.QuoteNumber).IsRequired().HasMaxLength(20);
            entity.HasIndex(q => q.QuoteNumber).IsUnique();

            entity.Property(q => q.Title).IsRequired().HasMaxLength(200);
            entity.Property(q => q.Description).HasMaxLength(4000);
            entity.Property(q => q.Notes).HasMaxLength(4000);

            entity.Property(q => q.Amount)
                .HasColumnType("numeric(10,2)");

            entity.Property(q => q.CreatedUtc)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("now()")
                .ValueGeneratedOnAdd();

            entity.Property(q => q.UpdatedUtc).HasColumnType("timestamptz");
            entity.Property(q => q.ExpiryDate).HasColumnType("timestamptz");
            entity.Property(q => q.SentUtc).HasColumnType("timestamptz");

            entity.HasIndex(q => q.ClientId);
            entity.HasIndex(q => q.Status);
            entity.HasIndex(q => q.ClientToken).IsUnique();
        });

        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.AS1851Section).HasMaxLength(50);
            entity.Property(s => s.ComplianceFormReference).HasMaxLength(50);

            entity.HasOne(s => s.DefaultInterval)
                .WithMany()
                .HasForeignKey(s => s.DefaultIntervalId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.Property(q => q.QuoteNumber).IsRequired().HasMaxLength(20);
            entity.HasIndex(q => q.QuoteNumber).IsUnique();
            entity.Property(q => q.Notes).HasMaxLength(4000);
            entity.Property(q => q.ExpiryDate).HasColumnType("timestamptz");
            entity.Property(q => q.CreatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("now()")
                .ValueGeneratedOnAdd();

            entity.HasOne(q => q.Client)
                .WithMany()
                .HasForeignKey(q => q.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(q => q.Site)
                .WithMany()
                .HasForeignKey(q => q.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(q => q.CreatedBy)
                .WithMany()
                .HasForeignKey(q => q.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(q => q.ClientId);
            entity.HasIndex(q => q.SiteId);
            entity.HasIndex(q => q.Status);
        });

        modelBuilder.Entity<QuoteLineItem>(entity =>
        {
            entity.Property(li => li.UnitPrice).HasColumnType("numeric(10,2)");

            entity.HasOne(li => li.Quote)
                .WithMany(q => q.LineItems)
                .HasForeignKey(li => li.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(li => li.Asset)
                .WithMany()
                .HasForeignKey(li => li.AssetId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(li => li.ServiceType)
                .WithMany()
                .HasForeignKey(li => li.ServiceTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(li => li.RecurringInterval)
                .WithMany()
                .HasForeignKey(li => li.RecurringIntervalId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.HasIndex(li => li.QuoteId);
        });
    }

}
