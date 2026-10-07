using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Persistence;

/// <summary>
/// Uses IdentityUserContext (not the full IdentityDbContext) because roles are
/// organization-scoped via OrganizationUser.Role rather than global Identity
/// roles — see 04_MYSQL_DATABASE_DESIGN.md organization_users table.
/// </summary>
public class GeoInsightDbContext(DbContextOptions<GeoInsightDbContext> options)
    : IdentityUserContext<ApplicationUser, long>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();

    public DbSet<Dataset> Datasets => Set<Dataset>();

    public DbSet<Import> Imports => Set<Import>();

    public DbSet<DatasetRecord> DatasetRecords => Set<DatasetRecord>();

    public DbSet<GeocodeJob> GeocodeJobs => Set<GeocodeJob>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<CensusTract> CensusTracts => Set<CensusTract>();

    public DbSet<CensusDemographic> CensusDemographics => Set<CensusDemographic>();

    public DbSet<CensusTractIncomeLevel> CensusTractIncomeLevels => Set<CensusTractIncomeLevel>();

    public DbSet<AssessmentAreaCounty> AssessmentAreaCounties => Set<AssessmentAreaCounty>();

    public DbSet<SavedView> SavedViews => Set<SavedView>();

    public DbSet<Export> Exports => Set<Export>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        });

        builder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.Property(o => o.Name).HasMaxLength(200).IsRequired();
        });

        builder.Entity<OrganizationUser>(entity =>
        {
            entity.ToTable("organization_users");
            entity.HasKey(ou => new { ou.OrganizationId, ou.UserId });

            entity.HasOne(ou => ou.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(ou => ou.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(ou => ou.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(ou => ou.Role)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        builder.Entity<Dataset>(entity =>
        {
            entity.ToTable("datasets");
            entity.Property(d => d.Name).HasMaxLength(200).IsRequired();
            entity.Property(d => d.DatasetType).HasMaxLength(50).IsRequired();

            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(d => d.OrganizationId).HasDatabaseName("idx_datasets_org");
        });

        builder.Entity<Import>(entity =>
        {
            entity.ToTable("imports");
            entity.Property(i => i.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(i => i.StoredFileName).HasMaxLength(255).IsRequired();
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne<Dataset>()
                .WithMany()
                .HasForeignKey(i => i.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(i => i.DatasetId).HasDatabaseName("idx_imports_dataset");
        });

        builder.Entity<DatasetRecord>(entity =>
        {
            entity.ToTable("dataset_records");
            entity.Property(r => r.AddressLine1).HasMaxLength(255).IsRequired();
            entity.Property(r => r.City).HasMaxLength(150).IsRequired();
            entity.Property(r => r.State).HasMaxLength(50).IsRequired();
            entity.Property(r => r.GeocodeStatus).HasMaxLength(40).IsRequired();
            entity.Property(r => r.Amount).HasPrecision(18, 2);
            entity.Property(r => r.Latitude).HasPrecision(10, 7);
            entity.Property(r => r.Longitude).HasPrecision(10, 7);
            entity.Property(r => r.GeocodeCandidates).HasColumnType("json");

            entity.HasOne<Dataset>()
                .WithMany()
                .HasForeignKey(r => r.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Import>()
                .WithMany()
                .HasForeignKey(r => r.ImportId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(r => r.DatasetId).HasDatabaseName("idx_records_dataset");
            entity.HasIndex(r => r.ImportId).HasDatabaseName("idx_records_import");
            entity.HasIndex(r => r.ExternalId).HasDatabaseName("idx_records_external_id");
            entity.HasIndex(r => r.GeocodeStatus).HasDatabaseName("idx_records_geocode_status");
        });

        builder.Entity<GeocodeJob>(entity =>
        {
            entity.ToTable("geocode_jobs");
            entity.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne<Dataset>()
                .WithMany()
                .HasForeignKey(j => j.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(j => j.DatasetId).HasDatabaseName("idx_geocode_job_dataset");
            entity.HasIndex(j => j.Status).HasDatabaseName("idx_geocode_job_status");
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.EntityType).HasMaxLength(100);
            entity.Property(a => a.EntityId).HasMaxLength(100);
            entity.Property(a => a.Metadata).HasColumnType("json");

            entity.HasIndex(a => new { a.OrganizationId, a.CreatedAt }).HasDatabaseName("idx_audit_org_date");
        });

        builder.Entity<CensusTract>(entity =>
        {
            entity.ToTable("census_tracts");
            entity.Property(t => t.Geoid).HasMaxLength(20).IsRequired();
            entity.Property(t => t.StateFips).HasMaxLength(2).IsRequired();
            entity.Property(t => t.CountyFips).HasMaxLength(3).IsRequired();
            entity.Property(t => t.TractCode).HasMaxLength(6).IsRequired();
            entity.Property(t => t.StateName).HasMaxLength(100);
            entity.Property(t => t.TractName).HasMaxLength(150);
            entity.Property(t => t.Boundary).HasColumnType("multipolygon");

            entity.HasIndex(t => t.Geoid).IsUnique().HasDatabaseName("uq_census_tract_geoid");
            entity.HasIndex(t => new { t.StateFips, t.CountyFips }).HasDatabaseName("idx_census_tract_county");
            entity.HasIndex(t => t.Boundary).HasDatabaseName("idx_census_tract_boundary").IsSpatial();
        });

        builder.Entity<CensusDemographic>(entity =>
        {
            entity.ToTable("census_demographics");
            entity.Property(d => d.CensusGeoid).HasMaxLength(20).IsRequired();
            entity.Property(d => d.MedianHouseholdIncome).HasPrecision(12, 2);
            entity.Property(d => d.PovertyRate).HasPrecision(6, 3);
            entity.Property(d => d.MedianAge).HasPrecision(5, 2);
            entity.Property(d => d.OwnerOccupiedPercent).HasPrecision(6, 3);
            entity.Property(d => d.RenterOccupiedPercent).HasPrecision(6, 3);
            entity.Property(d => d.MinorityPopulationPercent).HasPrecision(6, 3);
            entity.Property(d => d.RawData).HasColumnType("json");

            entity.HasIndex(d => new { d.CensusGeoid, d.SurveyYear }).IsUnique().HasDatabaseName("uq_demographics_geoid_year");
            entity.HasIndex(d => d.SurveyYear).HasDatabaseName("idx_demographics_year");
        });

        builder.Entity<CensusTractIncomeLevel>(entity =>
        {
            entity.ToTable("census_tract_income_levels");
            entity.Property(l => l.CensusGeoid).HasMaxLength(20).IsRequired();
            entity.Property(l => l.SurveyPeriod).HasMaxLength(20).IsRequired();
            entity.Property(l => l.TractMedianFamilyIncome).HasPrecision(12, 2);
            entity.Property(l => l.AreaMedianFamilyIncome).HasPrecision(12, 2);
            entity.Property(l => l.IncomePercentageOfAreaMedian).HasPrecision(7, 2);
            entity.Property(l => l.MsaMdName).HasMaxLength(200);

            entity.HasIndex(l => new { l.CensusGeoid, l.SurveyPeriod }).IsUnique().HasDatabaseName("uq_income_level_geoid_period");
        });

        builder.Entity<AssessmentAreaCounty>(entity =>
        {
            entity.ToTable("assessment_area_counties");
            entity.Property(a => a.StateFips).HasMaxLength(2).IsRequired();
            entity.Property(a => a.CountyFips).HasMaxLength(3).IsRequired();

            entity.HasIndex(a => new { a.OrganizationId, a.StateFips, a.CountyFips })
                .IsUnique()
                .HasDatabaseName("uq_assessment_area_org_county");
        });

        builder.Entity<SavedView>(entity =>
        {
            entity.ToTable("saved_views");
            entity.Property(v => v.Name).HasMaxLength(200).IsRequired();
            entity.Property(v => v.Filters).HasColumnType("json");
            entity.Property(v => v.MapConfiguration).HasColumnType("json");
            entity.Property(v => v.VisibleColumns).HasColumnType("json");

            entity.HasIndex(v => v.DatasetId).HasDatabaseName("idx_saved_views_dataset");
            entity.HasIndex(v => v.OrganizationId).HasDatabaseName("idx_saved_views_org");
        });

        builder.Entity<Export>(entity =>
        {
            entity.ToTable("exports");
            entity.Property(e => e.ExportType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.Filters).HasColumnType("json");

            entity.HasIndex(e => e.DatasetId).HasDatabaseName("idx_exports_dataset");
        });
    }
}
