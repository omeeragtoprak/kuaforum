namespace Kuaforum.Infrastructure.Storage;

/// <summary>
/// Provider switch for <c>IObjectStorage</c>: <c>"Local"</c> (default — dev/CI disk,
/// see <see cref="LocalFileObjectStorage"/>) or <c>"S3"</c> (Cloudflare R2 in production,
/// MinIO in integration tests — see <see cref="S3ObjectStorage"/>). Kept defaulted to
/// "Local" even in appsettings.Production.json so prod boot doesn't break before the
/// founder provisions an R2 bucket (GAP P1-6) — see infra/README.md "ObjectStorage".
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Provider { get; set; } = "Local";

    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class S3StorageOptions
{
    /// <summary>S3-compatible endpoint. R2: <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>. MinIO: <c>http://localhost:9000</c>.</summary>
    public string? ServiceUrl { get; set; }

    public string? Bucket { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    /// <summary>SigV4 signing region. R2 ignores the value but the SDK requires one — Cloudflare's own docs use "auto". MinIO also accepts any value.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// R2 does not support the SDK's default streaming/chunked SigV4 payload signing
    /// (PutObject fails with "STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented" —
    /// a known Cloudflare R2 + AWSSDK.S3 incompatibility). Set true for R2. MinIO supports
    /// standard chunked signing so tests leave this false. Requires HTTPS per AWS SDK docs
    /// (R2's endpoint always is); do not enable against a plain-HTTP endpoint.
    /// </summary>
    public bool DisablePayloadSigning { get; set; }
}
