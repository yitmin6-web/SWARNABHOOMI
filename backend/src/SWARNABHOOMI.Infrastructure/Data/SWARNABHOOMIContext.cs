using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;

namespace SWARNABHOOMI.Infrastructure.Data;

public class SWARNABHOOMIContext : DbContext
{
    public SWARNABHOOMIContext(DbContextOptions<SWARNABHOOMIContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<FarmerProfile> FarmerProfiles { get; set; } = null!;
    public DbSet<BuyerProfile> BuyerProfiles { get; set; } = null!;
    public DbSet<GovernmentProfile> GovernmentProfiles { get; set; } = null!;
    public DbSet<OfficerAssignment> OfficerAssignments { get; set; } = null!;
    public DbSet<Farm> Farms { get; set; } = null!;
    public DbSet<Field> Fields { get; set; } = null!;
    public DbSet<FieldVerification> FieldVerifications { get; set; } = null!;
    public DbSet<Season> Seasons { get; set; } = null!;
    public DbSet<ResourceRuleVersion> ResourceRuleVersions { get; set; } = null!;
    public DbSet<AiAssistantConfiguration> AiAssistantConfigurations { get; set; } = null!;
    public DbSet<RiceVariety> RiceVarieties { get; set; } = null!;
    public DbSet<CultivationRecord> CultivationRecords { get; set; } = null!;
    public DbSet<Demand> Demands { get; set; } = null!;
    public DbSet<DemandItem> DemandItems { get; set; } = null!;
    public DbSet<HarvestForecast> HarvestForecasts { get; set; } = null!;
    public DbSet<HarvestRecord> HarvestRecords { get; set; } = null!;
    public DbSet<MarketPriceObservation> MarketPriceObservations { get; set; } = null!;
    public DbSet<StockRecord> StockRecords { get; set; } = null!;
    public DbSet<PurchaseRequest> PurchaseRequests { get; set; } = null!;
    public DbSet<WeatherObservation> WeatherObservations { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<DataSource> DataSources { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureProfiles(modelBuilder);
        ConfigureFarmsFields(modelBuilder);
        ConfigureExtensionFoundation(modelBuilder);
        ConfigureVarieties(modelBuilder);
        ConfigureCultivation(modelBuilder);
        ConfigureDemand(modelBuilder);
        ConfigureForecastsHarvest(modelBuilder);
        ConfigurePricesStock(modelBuilder);
        ConfigurePurchaseNotifications(modelBuilder);
        ConfigureAuditDataSource(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Role);
            entity.HasIndex(u => u.IsActive);
            entity.HasIndex(u => u.District);

            entity.Property(u => u.Email).HasMaxLength(256);
            entity.Property(u => u.FirstName).HasMaxLength(100);
            entity.Property(u => u.LastName).HasMaxLength(100);
            entity.Property(u => u.PhoneNumber).HasMaxLength(30);
            entity.Property(u => u.District).HasMaxLength(120);
            entity.Property(u => u.Province).HasMaxLength(120);
        });
    }

    private static void ConfigureProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FarmerProfile>(entity =>
        {
            entity.HasIndex(fp => fp.UserId).IsUnique();
            entity.HasIndex(fp => fp.NationalId).IsUnique();
            entity.HasOne(fp => fp.User)
                .WithOne(u => u.FarmerProfile)
                .HasForeignKey<FarmerProfile>(fp => fp.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BuyerProfile>(entity =>
        {
            entity.HasIndex(bp => bp.UserId).IsUnique();
            entity.HasOne(bp => bp.User)
                .WithOne(u => u.BuyerProfile)
                .HasForeignKey<BuyerProfile>(bp => bp.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GovernmentProfile>(entity =>
        {
            entity.HasIndex(gp => gp.UserId).IsUnique();
            entity.HasOne(gp => gp.User)
                .WithOne(u => u.GovernmentProfile)
                .HasForeignKey<GovernmentProfile>(gp => gp.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OfficerAssignment>(entity =>
        {
            entity.HasIndex(oa => oa.UserId);
            entity.HasIndex(oa => oa.District);
            entity.HasOne(oa => oa.User)
                .WithMany(u => u.OfficerAssignments)
                .HasForeignKey(oa => oa.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureFarmsFields(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Farm>(entity =>
        {
            entity.HasIndex(f => f.UserId);
            entity.HasIndex(f => f.District);
            entity.HasOne(f => f.User)
                .WithMany(u => u.Farms)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Field>(entity =>
        {
            entity.HasIndex(f => f.FarmId);
            entity.HasOne(f => f.Farm)
                .WithMany(f => f.Fields)
                .HasForeignKey(f => f.FarmId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureVarieties(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiceVariety>(entity =>
        {
            entity.HasIndex(rv => rv.VarietyCode).IsUnique();
            entity.HasIndex(rv => rv.IsActive);

            entity.Property(rv => rv.VarietyCode).HasMaxLength(50);
            entity.Property(rv => rv.Name).HasMaxLength(200);
            entity.Property(rv => rv.Description).HasMaxLength(500);
        });
    }

    private static void ConfigureExtensionFoundation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldVerification>(entity =>
        {
            entity.HasIndex(verification => new { verification.FieldId, verification.CreatedAt });
            entity.Property(verification => verification.Status).HasMaxLength(30);
            entity.Property(verification => verification.ReviewerId).HasMaxLength(100);
            entity.Property(verification => verification.ReviewNotes).HasMaxLength(2000);
            entity.HasOne(verification => verification.Field)
                .WithMany(field => field.Verifications)
                .HasForeignKey(verification => verification.FieldId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasIndex(season => new { season.Year, season.District, season.AgrarianServiceCentre, season.Status });
            entity.Property(season => season.Name).HasMaxLength(100);
            entity.Property(season => season.District).HasMaxLength(120);
            entity.Property(season => season.AgrarianServiceCentre).HasMaxLength(120);
            entity.Property(season => season.WaterScheduleReference).HasMaxLength(500);
            entity.Property(season => season.ApprovedVarietyClassesJson).HasMaxLength(4000);
            entity.Property(season => season.Status).HasMaxLength(30);
            entity.Property(season => season.ApprovedBy).HasMaxLength(100);
            entity.Property(season => season.ClosureReason).HasMaxLength(1000);
        });

        modelBuilder.Entity<ResourceRuleVersion>(entity =>
        {
            entity.HasIndex(rule => new { rule.Category, rule.Status, rule.EffectiveFrom, rule.EffectiveTo });
            entity.Property(rule => rule.Category).HasMaxLength(40);
            entity.Property(rule => rule.ApplicabilityJson).HasMaxLength(4000);
            entity.Property(rule => rule.Unit).HasMaxLength(40);
            entity.Property(rule => rule.SourceReference).HasMaxLength(500);
            entity.Property(rule => rule.Status).HasMaxLength(30);
            entity.Property(rule => rule.ReviewedBy).HasMaxLength(100);
            entity.Property(rule => rule.ChangeReason).HasMaxLength(1000);
        });

        modelBuilder.Entity<AiAssistantConfiguration>(entity =>
        {
            entity.HasIndex(configuration => configuration.CreatedAt);
            entity.Property(configuration => configuration.BaseUrl).HasMaxLength(500);
            entity.Property(configuration => configuration.Model).HasMaxLength(200);
            entity.Property(configuration => configuration.EncryptedApiKey).HasMaxLength(4000);
        });
    }

    private static void ConfigureCultivation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CultivationRecord>(entity =>
        {
            entity.HasIndex(cr => cr.FarmerId);
            entity.HasIndex(cr => cr.FieldId);
            entity.HasIndex(cr => cr.RiceVarietyId);
            entity.HasIndex(cr => cr.Season);

            entity.HasOne(cr => cr.Farmer)
                .WithMany(u => u.CultivationRecords)
                .HasForeignKey(cr => cr.FarmerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cr => cr.Field)
                .WithMany(f => f.CultivationRecords)
                .HasForeignKey(cr => cr.FieldId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cr => cr.RiceVariety)
                .WithMany(rv => rv.CultivationRecords)
                .HasForeignKey(cr => cr.RiceVarietyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(cr => cr.Season).HasMaxLength(50);
            entity.Property(cr => cr.Status).HasMaxLength(30);
            entity.Property(cr => cr.Notes).HasMaxLength(1000);
        });
    }

    private static void ConfigureDemand(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Demand>(entity =>
        {
            entity.HasIndex(d => d.OwnerId);
            entity.HasIndex(d => d.District);
            entity.HasIndex(d => d.Status);

            entity.HasOne(d => d.Owner)
                .WithMany(u => u.Demands)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(d => d.District).HasMaxLength(120);
            entity.Property(d => d.Season).HasMaxLength(50);
            entity.Property(d => d.Status).HasMaxLength(30);
            entity.Property(d => d.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<DemandItem>(entity =>
        {
            entity.HasIndex(di => di.DemandId);
            entity.HasIndex(di => di.RiceVarietyId);

            entity.HasOne(di => di.Demand)
                .WithMany(d => d.DemandItems)
                .HasForeignKey(di => di.DemandId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(di => di.RiceVariety)
                .WithMany(rv => rv.DemandItems)
                .HasForeignKey(di => di.RiceVarietyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(di => di.Grade).HasMaxLength(50);
            entity.Property(di => di.Notes).HasMaxLength(500);
        });
    }

    private static void ConfigureForecastsHarvest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HarvestForecast>(entity =>
        {
            entity.HasIndex(hf => hf.CultivationId);
            entity.HasIndex(hf => hf.ConfidenceCategory);

            entity.HasOne(hf => hf.CultivationRecord)
                .WithMany(cr => cr.HarvestForecasts)
                .HasForeignKey(hf => hf.CultivationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(hf => hf.RunVersion).HasMaxLength(50);
            entity.Property(hf => hf.MethodVersion).HasMaxLength(50);
            entity.Property(hf => hf.ConfidenceCategory).HasMaxLength(30);
            entity.Property(hf => hf.Assumptions).HasMaxLength(1000);
        });

        modelBuilder.Entity<HarvestRecord>(entity =>
        {
            entity.HasIndex(hr => hr.CultivationId);
            entity.HasIndex(hr => hr.VerificationStatus);

            entity.HasOne(hr => hr.CultivationRecord)
                .WithMany(cr => cr.HarvestRecords)
                .HasForeignKey(hr => hr.CultivationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(hr => hr.Quality).HasMaxLength(50);
            entity.Property(hr => hr.EvidenceReference).HasMaxLength(500);
            entity.Property(hr => hr.VerificationStatus).HasMaxLength(30);
        });
    }

    private static void ConfigurePricesStock(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketPriceObservation>(entity =>
        {
            entity.HasIndex(mpo => mpo.RiceVarietyId);
            entity.HasIndex(mpo => mpo.ObservationDate);
            entity.HasIndex(mpo => mpo.Market);

            entity.HasOne(mpo => mpo.RiceVariety)
                .WithMany(rv => rv.PriceObservations)
                .HasForeignKey(mpo => mpo.RiceVarietyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(mpo => mpo.Market).HasMaxLength(120);
            entity.Property(mpo => mpo.Grade).HasMaxLength(50);
            entity.Property(mpo => mpo.PriceType).HasMaxLength(30);
            entity.Property(mpo => mpo.Currency).HasMaxLength(20);
            entity.Property(mpo => mpo.Unit).HasMaxLength(20);
            entity.Property(mpo => mpo.Source).HasMaxLength(200);
        });

        modelBuilder.Entity<StockRecord>(entity =>
        {
            entity.HasIndex(sr => sr.RiceVarietyId);
            entity.HasIndex(sr => sr.AsOfDate);
            entity.HasIndex(sr => sr.Location);

            entity.HasOne(sr => sr.RiceVariety)
                .WithMany()
                .HasForeignKey(sr => sr.RiceVarietyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(sr => sr.Location).HasMaxLength(200);
            entity.Property(sr => sr.Grade).HasMaxLength(50);
            entity.Property(sr => sr.CommodityStage).HasMaxLength(30);
            entity.Property(sr => sr.Unit).HasMaxLength(20);
            entity.Property(sr => sr.VerificationStatus).HasMaxLength(30);
        });
    }

    private static void ConfigurePurchaseNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseRequest>(entity =>
        {
            entity.HasIndex(pr => pr.BuyerId);
            entity.HasIndex(pr => pr.FarmerId);
            entity.HasIndex(pr => pr.DemandItemId);
            entity.HasIndex(pr => pr.Status);

            entity.HasOne(pr => pr.Buyer)
                .WithMany(u => u.PurchaseRequests)
                .HasForeignKey(pr => pr.BuyerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pr => pr.Farmer)
                .WithMany()
                .HasForeignKey(pr => pr.FarmerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(pr => pr.DemandItem)
                .WithMany()
                .HasForeignKey(pr => pr.DemandItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(pr => pr.Status).HasMaxLength(30);
            entity.Property(pr => pr.Terms).HasMaxLength(1000);
            entity.Property(pr => pr.DeliveryTerms).HasMaxLength(500);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(n => n.UserId);
            entity.HasIndex(n => n.IsRead);
            entity.HasIndex(n => n.Type);

            entity.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(n => n.Type).HasMaxLength(50);
            entity.Property(n => n.MessageKey).HasMaxLength(200);
            entity.Property(n => n.Payload).HasMaxLength(2000);
            entity.Property(n => n.DeliveryStatus).HasMaxLength(50);
        });
    }

    private static void ConfigureAuditDataSource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(al => al.Timestamp);
            entity.HasIndex(al => al.Action);
            entity.HasIndex(al => al.EntityType);
            entity.HasIndex(al => al.Actor);

            entity.Property(al => al.Action).HasMaxLength(100);
            entity.Property(al => al.EntityType).HasMaxLength(100);
            entity.Property(al => al.Result).HasMaxLength(50);
            entity.Property(al => al.Reason).HasMaxLength(500);
            entity.Property(al => al.SafeMetadata).HasMaxLength(2000);
        });

        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.HasIndex(ds => ds.Provider);
            entity.HasIndex(ds => ds.Dataset);

            entity.Property(ds => ds.Provider).HasMaxLength(100);
            entity.Property(ds => ds.Dataset).HasMaxLength(150);
            entity.Property(ds => ds.LicenseTermsNote).HasMaxLength(1000);
            entity.Property(ds => ds.ImportBatch).HasMaxLength(100);
            entity.Property(ds => ds.ValidationResults).HasMaxLength(2000);
        });
    }
}