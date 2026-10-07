using Microsoft.AspNetCore.DataProtection;
using GeoInsight.Application.Analytics;
using GeoInsight.Application.AssessmentArea;
using GeoInsight.Application.Audit;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Common;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Exports;
using GeoInsight.Application.Geocoding;
using GeoInsight.Application.HmdaBenchmark;
using GeoInsight.Application.Imports;
using GeoInsight.Application.Organizations;
using GeoInsight.Application.Records;
using GeoInsight.Application.Review;
using GeoInsight.Application.SavedViews;
using GeoInsight.Application.Tracts;
using GeoInsight.Infrastructure.Analytics;
using GeoInsight.Infrastructure.AssessmentArea;
using GeoInsight.Infrastructure.Audit;
using GeoInsight.Infrastructure.Auth;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Exports;
using GeoInsight.Infrastructure.Geocoding;
using GeoInsight.Infrastructure.HmdaBenchmark;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Imports;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using GeoInsight.Infrastructure.Review;
using GeoInsight.Infrastructure.SavedViews;
using GeoInsight.Infrastructure.Storage;
using GeoInsight.Infrastructure.Tracts;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoInsight.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Hardcoded (not auto-detected) server version so `dotnet ef migrations add`
    /// works without a live MySQL connection available at design time.
    /// </summary>
    private static readonly Version MySqlTargetVersion = new(8, 0, 36);

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default configuration value.");

        services.AddDbContext<GeoInsightDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(MySqlTargetVersion), mySqlOptions => mySqlOptions.UseNetTopologySuite()));

        // AddDefaultTokenProviders() (password reset) needs IDataProtectionProvider.
        // The Api project gets one for free from its ASP.NET Core web host; the
        // Worker (a plain generic host, no web pipeline) does not — so it's
        // registered explicitly here, in the DI setup they both share, rather
        // than relying on either host to provide it implicitly.
        // SetApplicationName is required, not cosmetic: without it, Data
        // Protection derives an application discriminator from each process's
        // own content-root path, so a value the Api encrypts (e.g. an
        // organization's Census API key, set via HTTP) can never be decrypted
        // by the Worker (which reads it back to run tract-import jobs) even
        // though both share the same underlying key ring on this machine —
        // confirmed live: a CryptographicException on first cross-process use.
        services.AddDataProtection().SetApplicationName("GeoInsight");

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<GeoInsightDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrganizationAccessControl, OrganizationAccessControl>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IDatasetService, DatasetService>();
        services.AddScoped<IDatasetAccessControl, DatasetAccessControl>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IDatasetRecordService, DatasetRecordService>();

        services.AddHttpClient<IGeocodingService, CensusGeocodingService>(client =>
        {
            client.BaseAddress = new Uri("https://geocoding.geo.census.gov/geocoder/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IGeocodeJobRunner, GeocodeJobRunner>();
        services.AddScoped<IGeocodeJobService, GeocodeJobService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IRecordReviewService, RecordReviewService>();

        services.AddHttpClient<IStateTractSource, TigerStateTractSource>(client =>
        {
            client.BaseAddress = new Uri("https://www2.census.gov/geo/tiger/TIGER2023/TRACT/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<ITractImportRunner, TractImportRunner>();
        services.AddScoped<ICensusTractImportService, CensusTractImportService>();
        // Static bundled reference data (a yearly FFIEC flat file) — a
        // singleton so the CSV is parsed once per process, not per request.
        services.AddSingleton<IFfiecTractIncomeSource, FfiecTractIncomeSource>();
        // Synthetic fallback, used when an organization has no Census API key
        // configured (or the real call fails) — see IDemographicDataSource's
        // doc comment.
        services.AddScoped<IDemographicDataSource, PlaceholderDemographicDataSource>();
        services.AddHttpClient<IAcsDemographicDataSource, AcsDemographicDataSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.census.gov/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IAssessmentAreaService, AssessmentAreaService>();
        services.AddHttpClient<IHmdaBenchmarkSource, HmdaBenchmarkSource>(client =>
        {
            client.BaseAddress = new Uri("https://ffiec.cfpb.gov/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IHmdaBenchmarkService, HmdaBenchmarkService>();
        services.AddScoped<ISavedViewService, SavedViewService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IAuditEventService, AuditEventService>();

        // Client-only here (no AddHangfireServer): the Api enqueues jobs but
        // never executes them, so an HTTP request is never blocked by
        // geocoding work — GeoInsight.Worker is the process that actually
        // calls AddHangfireServer(). Both point at the same MySQL storage.
        services.AddHangfire(config => config.UseStorage(new MySqlStorage(
            connectionString,
            new MySqlStorageOptions { TablesPrefix = "Hangfire", PrepareSchemaIfNecessary = true })));

        return services;
    }
}
