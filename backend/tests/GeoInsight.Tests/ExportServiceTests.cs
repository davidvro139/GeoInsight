using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using GeoInsight.Application.Common;
using GeoInsight.Application.Exports;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Exports;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GeoInsight.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "geoinsight-export-tests-" + Guid.NewGuid());

    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required ExportService Service { get; init; }

        public static Harness Create(string tempDir)
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsRoot"] = tempDir })
                .Build();
            var fileStorage = new LocalFileStorageService(configuration);

            return new Harness
            {
                DbContext = dbContext,
                Service = new ExportService(dbContext, datasetAccessControl, fileStorage, new AuditService(dbContext)),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<(Organization Org, Dataset Dataset)> AddOrgAndDatasetAsync(ApplicationUser user, OrganizationRole role)
        {
            var org = new Organization { Name = "Acme", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            org.Members.Add(new OrganizationUser { UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow });
            DbContext.Organizations.Add(org);
            await DbContext.SaveChangesAsync();

            var dataset = new Dataset
            {
                OrganizationId = org.Id, Name = "Loans", CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            DbContext.Datasets.Add(dataset);
            await DbContext.SaveChangesAsync();

            return (org, dataset);
        }

        public async Task AddRecordAsync(
            long datasetId, string category, decimal? amount, decimal? lat = null, decimal? lon = null, string? geoid = null)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
                Category = category, Amount = amount, GeocodeStatus = lat != null ? "matched" : "pending",
                Latitude = lat, Longitude = lon, CensusGeoid = geoid,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddDemographicAsync(string geoid, int population, decimal medianHouseholdIncome)
        {
            DbContext.CensusDemographics.Add(new CensusDemographic
            {
                CensusGeoid = geoid, SurveyYear = 2022, Population = population, MedianHouseholdIncome = medianHouseholdIncome,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Reads the download's full content into memory and disposes the
    /// underlying file stream immediately — LocalFileStorageService.OpenRead
    /// holds a real Windows file lock until disposed, which would otherwise
    /// block Dispose()'s temp-directory cleanup.
    /// </summary>
    private static async Task<byte[]> ReadDownloadedBytesAsync(ExportFileDto file)
    {
        using var content = file.Content;
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static async Task<string> ReadDownloadedTextAsync(ExportFileDto file) =>
        Encoding.UTF8.GetString(await ReadDownloadedBytesAsync(file));

    [Fact]
    public async Task Csv_export_respects_filters_and_includes_demographic_columns()
    {
        var h = Harness.Create(_tempDir);
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddRecordAsync(dataset.Id, "Purchase", 100_000m, 41.2m, -111.9m, "49057000100");
        await h.AddRecordAsync(dataset.Id, "Refinance", 50_000m, 41.3m, -111.8m, "49057000200");
        await h.AddDemographicAsync("49057000100", 4000, 55_000m);

        var unfiltered = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.Csv, new RecordFilterRequest(), null), CancellationToken.None);
        var filtered = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.Csv, new RecordFilterRequest(Category: "Purchase"), null), CancellationToken.None);

        Assert.True(unfiltered.Succeeded);
        Assert.True(filtered.Succeeded);

        var unfilteredFile = await h.Service.DownloadAsync(org.Id, dataset.Id, unfiltered.Value!.Id, user.Id, CancellationToken.None);
        var filteredFile = await h.Service.DownloadAsync(org.Id, dataset.Id, filtered.Value!.Id, user.Id, CancellationToken.None);

        var unfilteredCsv = await ReadDownloadedTextAsync(unfilteredFile.Value!);
        var filteredCsv = await ReadDownloadedTextAsync(filteredFile.Value!);

        Assert.Equal(3, unfilteredCsv.TrimEnd().Split('\n').Length); // header + 2 rows
        Assert.Equal(2, filteredCsv.TrimEnd().Split('\n').Length); // header + 1 row
        Assert.Contains("55000", filteredCsv); // the Purchase record's tract income made it in

        Assert.Equal(2, h.DbContext.AuditEvents.Count());
        Assert.All(h.DbContext.AuditEvents, e => Assert.Equal("export.create", e.Action));
        Assert.All(h.DbContext.AuditEvents, e => Assert.Equal("export", e.EntityType));
    }

    [Fact]
    public async Task Xlsx_export_produces_a_valid_workbook_with_the_right_row_count()
    {
        var h = Harness.Create(_tempDir);
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddRecordAsync(dataset.Id, "Purchase", 100_000m);
        await h.AddRecordAsync(dataset.Id, "Refinance", 50_000m);

        var created = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.Xlsx, new RecordFilterRequest(), null), CancellationToken.None);
        Assert.True(created.Succeeded);

        var file = await h.Service.DownloadAsync(org.Id, dataset.Id, created.Value!.Id, user.Id, CancellationToken.None);
        var bytes = await ReadDownloadedBytesAsync(file.Value!);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);

        Assert.Equal("External ID", sheet.Cell(1, 1).GetString());
        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber()); // header + 2 records
    }

    [Fact]
    public async Task GeoJson_export_only_includes_geocoded_records_as_valid_points()
    {
        var h = Harness.Create(_tempDir);
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddRecordAsync(dataset.Id, "Purchase", 100_000m, 41.2m, -111.9m);
        await h.AddRecordAsync(dataset.Id, "Refinance", 50_000m); // no coordinates — pending

        var created = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.GeoJson, new RecordFilterRequest(), null), CancellationToken.None);
        var file = await h.Service.DownloadAsync(org.Id, dataset.Id, created.Value!.Id, user.Id, CancellationToken.None);
        var json = await ReadDownloadedTextAsync(file.Value!);

        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("features");
        Assert.Equal(1, features.GetArrayLength());
        var geometry = features[0].GetProperty("geometry");
        Assert.Equal("Point", geometry.GetProperty("type").GetString());
        var coordinates = geometry.GetProperty("coordinates");
        Assert.Equal(-111.9, coordinates[0].GetDouble(), precision: 5);
        Assert.Equal(41.2, coordinates[1].GetDouble(), precision: 5);
    }

    [Fact]
    public async Task Png_export_round_trips_the_given_bytes_exactly()
    {
        var h = Harness.Create(_tempDir);
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var originalBytes = Encoding.UTF8.GetBytes("not a real png, just test bytes");
        var base64 = Convert.ToBase64String(originalBytes);

        var created = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.Png, null, base64), CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.Equal("Png", created.Value!.ExportType);

        var file = await h.Service.DownloadAsync(org.Id, dataset.Id, created.Value.Id, user.Id, CancellationToken.None);
        var downloadedBytes = await ReadDownloadedBytesAsync(file.Value!);

        Assert.Equal(originalBytes, downloadedBytes);
        Assert.Equal("image/png", file.Value!.ContentType);
    }

    [Fact]
    public async Task Png_export_without_file_content_is_a_validation_error()
    {
        var h = Harness.Create(_tempDir);
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);

        var result = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateExportRequest(ExportType.Png, null, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task A_viewer_is_forbidden_from_creating_an_export_but_can_list_and_download()
    {
        var h = Harness.Create(_tempDir);
        var analyst = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(analyst, OrganizationRole.Analyst);
        var created = await h.Service.CreateAsync(
            org.Id, dataset.Id, analyst.Id, new CreateExportRequest(ExportType.Csv, new RecordFilterRequest(), null), CancellationToken.None);

        var viewer = await h.AddUserAsync("viewer@example.com");
        DbContext_AddMember(h, org, viewer, OrganizationRole.Viewer);

        var createByViewer = await h.Service.CreateAsync(
            org.Id, dataset.Id, viewer.Id, new CreateExportRequest(ExportType.Csv, new RecordFilterRequest(), null), CancellationToken.None);
        Assert.False(createByViewer.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, createByViewer.Error!.Type);

        var listByViewer = await h.Service.GetAllAsync(org.Id, dataset.Id, viewer.Id, CancellationToken.None);
        Assert.True(listByViewer.Succeeded);
        Assert.Single(listByViewer.Value!);

        var downloadByViewer = await h.Service.DownloadAsync(org.Id, dataset.Id, created.Value!.Id, viewer.Id, CancellationToken.None);
        Assert.True(downloadByViewer.Succeeded);
        downloadByViewer.Value!.Content.Dispose();
    }

    private static void DbContext_AddMember(Harness h, Organization org, ApplicationUser user, OrganizationRole role)
    {
        h.DbContext.OrganizationUsers.Add(new OrganizationUser
        {
            OrganizationId = org.Id, UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow,
        });
        h.DbContext.SaveChanges();
    }

    [Fact]
    public async Task An_export_from_another_organization_is_not_found()
    {
        var h = Harness.Create(_tempDir);
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (orgA, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);
        var created = await h.Service.CreateAsync(
            orgA.Id, datasetA.Id, userA.Id, new CreateExportRequest(ExportType.Csv, new RecordFilterRequest(), null), CancellationToken.None);

        var result = await h.Service.DownloadAsync(orgB.Id, datasetA.Id, created.Value!.Id, userB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }
}
