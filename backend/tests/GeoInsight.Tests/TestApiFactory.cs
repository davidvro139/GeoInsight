using GeoInsight.Application.Geocoding;
using GeoInsight.Application.Tracts;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoInsight.Tests;

/// <summary>
/// Runs the real Program.cs pipeline (controllers, JWT auth, JSON options) but
/// swaps the MySQL DbContext registration for EF Core InMemory, so the full
/// HTTP stack can be exercised without a live MySQL server — this is what
/// catches bugs that only show up through real HTTP/JWT/JSON, like a claim
/// type not matching what a controller reads, or an enum serializing as a raw
/// int instead of its name.
/// </summary>
public class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-tests-{Guid.NewGuid()}";
    private readonly string _uploadsRoot = Path.Combine(Path.GetTempPath(), $"geoinsight-api-tests-uploads-{Guid.NewGuid()}");

    /// <summary>
    /// Lets a test control what IGeocodingService returns (e.g. for exercising
    /// RecordReviewController's correct-address endpoint) without ever hitting
    /// the real Census API from the test process. Defaults to NoMatch.
    /// </summary>
    public Func<GeocodeQuery, GeocodeResult> GeocodeResponse { get; set; } = _ => GeocodeResult.NoMatch();

    private sealed class DelegatingGeocodingService(TestApiFactory factory) : IGeocodingService
    {
        public Task<GeocodeResult> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(factory.GeocodeResponse(query));
    }

    /// <summary>
    /// Lets a test control what tract features a state "download" returns
    /// without ever hitting the real Census TIGER service. Defaults to empty.
    /// </summary>
    public Func<string, IReadOnlyList<ParsedTractFeature>> TractResponse { get; set; } = _ => [];

    private sealed class DelegatingStateTractSource(TestApiFactory factory) : IStateTractSource
    {
        public Task<IReadOnlyList<ParsedTractFeature>> GetTractsAsync(string stateFips, CancellationToken cancellationToken) =>
            Task.FromResult(factory.TractResponse(stateFips));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
        });

        // Redirect file uploads to a temp directory instead of the dev uploads/
        // folder appsettings.json points at — otherwise every test run leaves
        // files behind in the real local dev storage.
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AutoMigrate"] = "false",
                ["Storage:UploadsRoot"] = _uploadsRoot,
            });
        });

        builder.ConfigureServices(services =>
        {
            // AddDbContext (EF Core 8+) registers the MySQL configuration as one of
            // possibly several chained IDbContextOptionsConfiguration<T> entries rather
            // than a single overwritable DbContextOptions<T> descriptor, so removing
            // just DbContextOptions<GeoInsightDbContext> leaves the MySQL config active
            // and EF throws "Only a single database provider can be registered" once
            // UseInMemoryDatabase is added alongside it. Purge every descriptor whose
            // service type closes over GeoInsightDbContext, whatever EF calls it.
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(GeoInsightDbContext)
                    || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(GeoInsightDbContext))))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<GeoInsightDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // Same reasoning as the DbContext swap above: AddHangfire's MySQL
            // storage registration would otherwise need a real MySQL server
            // reachable from the test process. Purge every Hangfire-namespaced
            // descriptor and re-register against Hangfire.MemoryStorage instead —
            // jobs enqueued in these tests just sit unexecuted, which is fine,
            // since GeocodeJobRunnerTests is what actually exercises job execution.
            var hangfireDescriptors = services
                .Where(d => d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in hangfireDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddHangfire(config => config.UseStorage(new MemoryStorage()));

            // Same reasoning again: never let a test hit the real Census API.
            // AddHttpClient<IGeocodingService, CensusGeocodingService> registers
            // several descriptors (the typed client plus the interface) — only
            // the IGeocodingService one needs removing; the unused HttpClient
            // factory registrations left behind are harmless.
            var geocodingDescriptors = services.Where(d => d.ServiceType == typeof(IGeocodingService)).ToList();
            foreach (var descriptor in geocodingDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IGeocodingService>(new DelegatingGeocodingService(this));

            var tractSourceDescriptors = services.Where(d => d.ServiceType == typeof(IStateTractSource)).ToList();
            foreach (var descriptor in tractSourceDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IStateTractSource>(new DelegatingStateTractSource(this));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_uploadsRoot))
        {
            Directory.Delete(_uploadsRoot, recursive: true);
        }
    }
}
