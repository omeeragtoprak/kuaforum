namespace Kuaforum.Infrastructure.Storage;

/// <summary>
/// Shared validation + storageKey-generation rules for <c>IObjectStorage</c> implementations.
/// Centralized here (rather than duplicated per-backend) so <see cref="LocalFileObjectStorage"/>
/// (dev/CI) and <see cref="S3ObjectStorage"/> (prod R2 / MinIO tests) behave identically —
/// same allowed content types, same size cap, same error messages, same storageKey shape —
/// regardless of which backend ObjectStorage:Provider selects. GAP P1-6.
/// </summary>
internal static class ObjectStorageRules
{
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>Throws <see cref="InvalidOperationException"/> (mapped to HTTP 400 by callers) on violation.</summary>
    public static void Validate(string contentType, long sizeBytes)
    {
        if (sizeBytes > MaxSizeBytes)
            throw new InvalidOperationException($"Yüklenen dosya 10 MB'tan büyük olamaz (boyut: {sizeBytes}).");
        if (!AllowedContentTypes.Contains(contentType))
            throw new InvalidOperationException($"Desteklenmeyen dosya tipi: {contentType}. PDF veya görüntü dosyası gerekli.");
    }

    public static string BuildStorageKey(string folder, string contentType, string? originalFileName)
    {
        var safeFolder = SafeSegment(folder);
        var ext = ResolveExtension(contentType, originalFileName);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        return $"{safeFolder}/{fileName}";
    }

    public static string SafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "misc";
        var trimmed = segment.Trim('/').ToLowerInvariant();
        var safe = new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/').ToArray());
        return string.IsNullOrEmpty(safe) ? "misc" : safe;
    }

    public static string ResolveExtension(string contentType, string? originalFileName)
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
