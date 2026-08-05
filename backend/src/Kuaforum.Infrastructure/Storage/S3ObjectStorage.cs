using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Storage;

/// <summary>
/// Production implementation of <see cref="IObjectStorage"/>, backed by any S3-compatible
/// object store — Cloudflare R2 in production, MinIO in integration tests (see
/// <c>ObjectStorageS3Tests</c>). Selected in DI instead of <see cref="LocalFileObjectStorage"/>
/// when <c>ObjectStorage:Provider=S3</c> (see DependencyInjection.AddInfrastructure). GAP P1-6.
///
/// Validation (allowed content types, 10 MB cap) and the storageKey shape are shared with
/// <see cref="LocalFileObjectStorage"/> via <see cref="ObjectStorageRules"/> so behavior is
/// identical regardless of which backend is wired up — callers (UploadsEndpoints,
/// AdminEndpoints) don't need to know or care which one is active.
///
/// Bucket lifecycle: this class does NOT create the bucket. It assumes the bucket already
/// exists — same assumption the app already makes about Postgres/Redis being reachable.
/// This is a deliberate choice, not an oversight: R2 API tokens are typically scoped to a
/// single pre-created bucket (least privilege — they can't call CreateBucket), so having the
/// app attempt bucket creation at runtime would either silently no-op or fail with a
/// confusing 403 depending on token scope. The founder creates the R2 bucket once via the
/// Cloudflare dashboard before flipping ObjectStorage:Provider to S3 (see infra/README.md).
/// Test fixtures create the MinIO bucket explicitly in their setup, mirroring that same
/// one-time manual step.
///
/// KYC documents are sensitive (kimlik, vergi levhası) — <see cref="GetReadableUrl"/>
/// returns a short-lived presigned GET URL, never a public/static URL.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage
{
    private static readonly TimeSpan PresignedUrlLifetime = TimeSpan.FromMinutes(15);

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _disablePayloadSigning;
    private readonly Protocol _presignedUrlProtocol;
    private readonly ILogger<S3ObjectStorage> _logger;

    public S3ObjectStorage(IAmazonS3 s3, IOptions<ObjectStorageOptions> options, ILogger<S3ObjectStorage> logger)
    {
        _s3 = s3;
        var s3Options = options.Value.S3;
        _bucket = s3Options.Bucket
            ?? throw new InvalidOperationException("ObjectStorage:S3:Bucket is required when ObjectStorage:Provider=S3.");
        _disablePayloadSigning = s3Options.DisablePayloadSigning;

        // GetPreSignedUrlRequest.Protocol defaults to HTTPS regardless of the client's
        // actual ServiceURL scheme — against a plain-HTTP endpoint (MinIO in tests/local
        // dev) that produces a URL whose TLS handshake fails even though the object itself
        // is reachable. Derive it from the configured ServiceUrl instead: R2 is always
        // https:// in production, MinIO is http:// in tests, so this "just matches reality"
        // in both cases rather than hardcoding either.
        _presignedUrlProtocol =
            Uri.TryCreate(s3Options.ServiceUrl, UriKind.Absolute, out var serviceUri)
            && string.Equals(serviceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? Protocol.HTTP
                : Protocol.HTTPS;

        _logger = logger;
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

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = storageKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false, // caller (UploadsEndpoints) owns and disposes the stream
            DisablePayloadSigning = _disablePayloadSigning ? true : null,
        };

        await _s3.PutObjectAsync(request, ct);

        _logger.LogInformation(
            "S3ObjectStorage: saved {StorageKey} ({SizeBytes} bytes, {ContentType}) to bucket {Bucket}.",
            storageKey, sizeBytes, contentType, _bucket);
        return storageKey;
    }

    public string GetReadableUrl(string storageKey)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Protocol = _presignedUrlProtocol,
            Expires = DateTime.UtcNow.Add(PresignedUrlLifetime),
        };
        return _s3.GetPreSignedURL(request);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, storageKey, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Dosya bulunamadı.", storageKey, ex);
        }
    }
}
