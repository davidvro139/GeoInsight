namespace GeoInsight.Application.Common;

/// <summary>
/// Local disk for MVP (LocalFileStorageService); swappable for S3-compatible
/// storage later without touching callers — see 03_TECHNICAL_ARCHITECTURE.md
/// "File Storage".
/// </summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string suggestedFileName, CancellationToken cancellationToken);

    Stream OpenRead(string storedFileName);

    void Delete(string storedFileName);
}
