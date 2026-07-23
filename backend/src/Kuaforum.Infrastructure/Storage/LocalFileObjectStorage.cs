using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Storage;

/// Dev/CI implementation of <see cref="IObjectStorage"/>. Writes uploads under
/// <c>{Storage:LocalRoot}</c> (default <c>./uploads</c>) and exposes them via the
/// <c>{Storage:PublicBaseUrl}</c> static-file mount. Production swap-out point:
/// register <c>R2ObjectStorage</c> in DI when R2 credentials are configured.
public sealed class LocalFileObjectStorage : IObjectStorage
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

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
        if (sizeBytes > MaxSizeBytes)
            throw new InvalidOperationException($"Yüklenen dosya 10 MB'tan büyük olamaz (boyut: {sizeBytes}).");
        if (!AllowedContentTypes.Contains(contentType))
            throw new InvalidOperationException($"Desteklenmeyen dosya tipi: {contentType}. PDF veya görüntü dosyası gerekli.");

        var safeFolder = SafeSegment(folder);
        var ext = ResolveExtension(contentType, originalFileName);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var storageKey = $"{safeFolder}/{fileName}";

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

    private static string SafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "misc";
        var trimmed = segment.Trim('/').ToLowerInvariant();
        var safe = new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/').ToArray());
        return string.IsNullOrEmpty(safe) ? "misc" : safe;
    }

    private static string ResolveExtension(string contentType, string? originalFileName)
    {
        if (!string.IsNullOrEmpty(originalFileName))
        {
            var ext = Path.GetExtension(originalFileName);
            if (!string.IsNullOrEmpty(ext) && ext.Length <= 8) return ext.ToLowerInvariant();
        }
        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => string.Empty,
        };
    }
}
