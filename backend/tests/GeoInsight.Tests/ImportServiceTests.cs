using System.Text;
using GeoInsight.Application.Common;
using GeoInsight.Application.Imports;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Imports;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GeoInsight.Tests;

public class ImportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "geoinsight-import-tests-" + Guid.NewGuid());
    private readonly GeoInsightDbContext _dbContext;
    private readonly ImportService _service;

    private static readonly IReadOnlyList<ColumnMapping> StandardMappings =
    [
        new(DatasetFieldCatalog.ExternalId, DatasetFieldCatalog.ExternalId),
        new(DatasetFieldCatalog.AddressLine1, DatasetFieldCatalog.AddressLine1),
        new(DatasetFieldCatalog.City, DatasetFieldCatalog.City),
        new(DatasetFieldCatalog.State, DatasetFieldCatalog.State),
        new(DatasetFieldCatalog.PostalCode, DatasetFieldCatalog.PostalCode),
    ];

    private const string SampleCsv =
        "external_id,address_line_1,city,state,postal_code\n" +
        "EXT1,123 Main St,Ogden,UT,84401\n" +
        "EXT2,456 Oak Ave,,UT,84401\n" +
        "EXT3,789 Pine Rd,Logan,UT,1234\n" +
        "EXT1,123 Main St,Ogden,UT,84401\n";

    public ImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new GeoInsightDbContext(options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsRoot"] = _tempDir })
            .Build();
        var fileStorage = new LocalFileStorageService(configuration);

        var organizationAccessControl = new OrganizationAccessControl(_dbContext);
        _service = new ImportService(
            _dbContext, new DatasetAccessControl(_dbContext, organizationAccessControl), fileStorage, new AuditService(_dbContext));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<(ApplicationUser User, Organization Org, Dataset Dataset)> SeedAsync(
        OrganizationRole role, string email = "user@example.com")
    {
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test User" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var org = new Organization { Name = "Acme", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        org.Members.Add(new OrganizationUser { UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow });
        _dbContext.Organizations.Add(org);
        await _dbContext.SaveChangesAsync();

        var dataset = new Dataset
        {
            OrganizationId = org.Id, Name = "Loans", CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _dbContext.Datasets.Add(dataset);
        await _dbContext.SaveChangesAsync();

        return (user, org, dataset);
    }

    private static Stream CsvStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Upload_detects_headers_and_row_count()
    {
        var (user, org, dataset) = await SeedAsync(OrganizationRole.Analyst);

        var result = await _service.UploadAsync(org.Id, dataset.Id, user.Id, CsvStream(SampleCsv), "loans.csv", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(",", result.Value!.DetectedDelimiter);
        Assert.Equal(["external_id", "address_line_1", "city", "state", "postal_code"], result.Value.Headers);
        Assert.Equal(4, result.Value.TotalRows);
    }

    [Fact]
    public async Task Validate_produces_expected_valid_warning_invalid_counts()
    {
        var (user, org, dataset) = await SeedAsync(OrganizationRole.Analyst);
        var upload = (await _service.UploadAsync(org.Id, dataset.Id, user.Id, CsvStream(SampleCsv), "loans.csv", CancellationToken.None)).Value!;

        var result = await _service.ValidateAsync(
            org.Id, dataset.Id, upload.ImportId, user.Id, new ImportValidateRequest(",", StandardMappings), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Value!.TotalRows);
        Assert.Equal(1, result.Value.ValidRows); // EXT1 row 1
        Assert.Equal(2, result.Value.WarningRows); // bad zip row, duplicate EXT1 row
        Assert.Equal(1, result.Value.InvalidRows); // missing city row
    }

    [Fact]
    public async Task Commit_inserts_only_valid_and_warning_rows()
    {
        var (user, org, dataset) = await SeedAsync(OrganizationRole.Analyst);
        var upload = (await _service.UploadAsync(org.Id, dataset.Id, user.Id, CsvStream(SampleCsv), "loans.csv", CancellationToken.None)).Value!;

        var result = await _service.CommitAsync(
            org.Id, dataset.Id, upload.ImportId, user.Id, new ImportValidateRequest(",", StandardMappings), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Committed", result.Value!.Status);

        var insertedCount = await _dbContext.DatasetRecords.CountAsync(r => r.DatasetId == dataset.Id);
        Assert.Equal(3, insertedCount); // 1 valid + 2 warning, the invalid row excluded

        var auditEvent = Assert.Single(_dbContext.AuditEvents);
        Assert.Equal("import.commit", auditEvent.Action);
        Assert.Equal("import", auditEvent.EntityType);
        Assert.Equal(upload.ImportId.ToString(), auditEvent.EntityId);
    }

    [Fact]
    public async Task Duplicate_external_id_against_an_already_committed_record_is_flagged()
    {
        var (user, org, dataset) = await SeedAsync(OrganizationRole.Analyst);
        _dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = dataset.Id,
            ExternalId = "EXT1",
            AddressLine1 = "Existing",
            City = "Ogden",
            State = "UT",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        const string csv = "external_id,address_line_1,city,state,postal_code\nEXT1,999 New St,Provo,UT,84601\n";
        var upload = (await _service.UploadAsync(org.Id, dataset.Id, user.Id, CsvStream(csv), "loans.csv", CancellationToken.None)).Value!;

        var result = await _service.ValidateAsync(
            org.Id, dataset.Id, upload.ImportId, user.Id, new ImportValidateRequest(",", StandardMappings), CancellationToken.None);

        Assert.Equal(0, result.Value!.ValidRows);
        Assert.Equal(1, result.Value.WarningRows);
        Assert.Contains(result.Value.Issues, i => i.Message.Contains("Duplicate external ID"));
    }

    [Fact]
    public async Task A_viewer_cannot_upload()
    {
        var (user, org, dataset) = await SeedAsync(OrganizationRole.Viewer);

        var result = await _service.UploadAsync(org.Id, dataset.Id, user.Id, CsvStream(SampleCsv), "loans.csv", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_found()
    {
        var (userA, orgA, datasetA) = await SeedAsync(OrganizationRole.Administrator, "a@example.com");
        var (userB, orgB, _) = await SeedAsync(OrganizationRole.Administrator, "b@example.com");

        var result = await _service.UploadAsync(orgB.Id, datasetA.Id, userB.Id, CsvStream(SampleCsv), "loans.csv", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }
}
