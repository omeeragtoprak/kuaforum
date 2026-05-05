namespace Kuaforum.Application.Common.Interfaces;

/// Cross-cutting blob storage abstraction. Backed by Cloudflare R2 in production
/// and a local-filesystem implementation in dev/CI. Returns a stable storageKey
/// that callers persist (e.g. KycDocument.StorageKey) and can be exchanged for a
/// readable URL via <see cref="GetReadableUrl"/>.
public interface IObjectStorage
{
    Task<string> SaveAsync(
        Stream content,
        string contentType,
        long sizeBytes,
        string folder,
        string? originalFileName,
        CancellationToken ct = default);

    string GetReadableUrl(string storageKey);
}
