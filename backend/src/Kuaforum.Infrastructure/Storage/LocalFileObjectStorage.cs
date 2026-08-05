using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Storage;

/// Dev/CI implementation of <see cref="IObjectStorage"/>. Writes uploads under
/// <c>{Storage:LocalRoot}</c> (default <c>./uploads</c>) and exposes them via the
/// <c>{Storage:PublicBaseUrl}</c> static-file mount. Not durable across container
/// redeploys — GAP P1-6. Selected when <c>ObjectStorage:Provider=Local</c> (default);
/// set it to <c>S3</c> to use <see cref="S3ObjectStorage"/> (Cloudflare R2 / MinIO)
/// instead — see DependencyInjection.AddInfrastructure.
public sealed class LocalFileObjectStorage : IObjectStorage
{
    private readonly string _localRoot;
    private readonly string _publicBaseUrl;
    private readonly ILogger<LocalFileObjectStorage> _logger;

    public LocalFileObjectStorage(IConfiguration configuration, ILogger<LocalFileObjectStorage> logger)
    {
        _logger = logger;
        _localRoot = configuration.GetValue<string>("Storage:LocalRoot") ?? Path.Combine(AppContext.BaseDirectory, "uploads");
        _publicBaseUrl = configuration.GetValue<string>("Storage:PublicBaseUrl") ?? "http://localhost:5000/uploads";
        Directory.CreateDirectory(_localRoot);
    }

    public async Task<string> SaveAsync(
        Stream content,
        string contentType,
        long sizeBytes,
        string folder,
        string? originalFileName,
        CancellationToken ct = default)
    {
        ObjectStorageRules.Validate(contentType, sizeBytes);

        var storageKey = ObjectStorageRules.BuildStorageKey(folder, contentType, originalFileName);
        var safeFolder = storageKey[..storageKey.LastIndexOf('/')];
        var fileName = storageKey[(storageKey.LastIndexOf('/') + 1)..];

        var folderPath = Path.Combine(_localRoot, safeFolder);
        Directory.CreateDirectory(folderPath);
        var fullPath = Path.Combine(folderPath, fileName);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true))
        {
            await content.CopyToAsync(fs, ct);
        }

        _logger.LogInformation("LocalFileObjectStorage: saved {StorageKey} ({SizeBytes} bytes, {ContentType}).",
            storageKey, sizeBytes, contentType);
        return storageKey;
    }

    public string GetReadableUrl(string storageKey)
        => $"{_publicBaseUrl.TrimEnd('/')}/{storageKey.TrimStart('/')}";

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var combined = Path.Combine(_localRoot, storageKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        var resolvedFull = Path.GetFullPath(combined);
        var resolvedRoot = Path.GetFullPath(_localRoot) + Path.DirectorySeparatorChar;

        // Guard against path traversal via a maliciously-crafted storageKey — resolved
        // path must stay inside _localRoot.
        if (!resolvedFull.StartsWith(resolvedRoot, StringComparison.Ordinal))
            throw new FileNotFoundException("Geçersiz depolama anahtarı.", storageKey);

        if (!File.Exists(resolvedFull))
            throw new FileNotFoundException("Dosya bulunamadı.", resolvedFull);

        Stream stream = new FileStream(
            resolvedFull, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        return Task.FromResult(stream);
    }
}
