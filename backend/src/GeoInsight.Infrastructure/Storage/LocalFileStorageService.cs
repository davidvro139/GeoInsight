using GeoInsight.Application.Common;
using Microsoft.Extensions.Configuration;

namespace GeoInsight.Infrastructure.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _root = configuration["Storage:UploadsRoot"] ?? "uploads";
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string suggestedFileName, CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(suggestedFileName);
        var storedFileName = $"{Guid.NewGuid():N}-{safeName}";
        var fullPath = Path.Combine(_root, storedFileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storedFileName;
    }

    public Stream OpenRead(string storedFileName) => File.OpenRead(Path.Combine(_root, storedFileName));

    public void Delete(string storedFileName)
    {
        var fullPath = Path.Combine(_root, storedFileName);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
