using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Kuaforum.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Owns a single throwaway MinIO container for the whole <see cref="ObjectStorageS3Tests"/>
/// class (via <see cref="ICollectionFixture{TFixture}"/> — same pattern as
/// <see cref="IntegrationTestFactory"/>/<see cref="IntegrationTestCollection"/>) so xUnit's
/// per-test-method instantiation doesn't spin up a fresh container per fact.
///
/// Bucket lifecycle decision (GAP P1-6): <see cref="S3ObjectStorage"/> deliberately never
/// creates buckets at runtime (see its class doc — least-privilege R2 tokens usually can't).
/// This fixture creates the test bucket explicitly once in <see cref="InitializeAsync"/>,
/// mirroring the one-time manual step the founder does in the Cloudflare R2 dashboard before
/// flipping ObjectStorage:Provider to S3 in production.
/// </summary>
public sealed class ObjectStorageS3Fixture : IAsyncLifetime
{
    public const string BucketName = "kuaforum-kyc-test";

    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:latest").Build();
    private IAmazonS3 _rawClient = null!;

    public S3ObjectStorage Storage { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var config = new AmazonS3Config
        {
            ServiceURL = _minio.GetConnectionString(),
            ForcePathStyle = true, // MinIO requires path-style addressing (no bucket subdomains)
            AuthenticationRegion = "auto",
        };
        _rawClient = new AmazonS3Client(_minio.GetAccessKey(), _minio.GetSecretKey(), config);

        await _rawClient.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });

        var options = Options.Create(new ObjectStorageOptions
        {
            Provider = "S3",
            S3 = new S3StorageOptions
            {
                ServiceUrl = _minio.GetConnectionString(),
                Bucket = BucketName,
                AccessKeyId = _minio.GetAccessKey(),
                SecretAccessKey = _minio.GetSecretKey(),
                // MinIO supports the SDK's default chunked SigV4 payload signing (unlike R2 —
                // see S3StorageOptions.DisablePayloadSigning doc), so leave this false here.
                DisablePayloadSigning = false,
            },
        });

        Storage = new S3ObjectStorage(_rawClient, options, NullLogger<S3ObjectStorage>.Instance);
    }

    public async Task DisposeAsync()
    {
        _rawClient.Dispose();
        await _minio.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ObjectStorageS3Collection))]
public sealed class ObjectStorageS3Collection : ICollectionFixture<ObjectStorageS3Fixture>
{
}

/// <summary>
/// Adapter-level proof that <see cref="S3ObjectStorage"/> works against a real (throwaway)
/// S3-compatible server — not just mocks. Runs standalone (own MinIO container via
/// <see cref="ObjectStorageS3Fixture"/>, not part of <see cref="IntegrationTestFactory"/>'s
/// shared Postgres/Redis collection) so unrelated test runs don't pay MinIO startup cost, and
/// existing KYC tests (<c>KycFlowTests</c>) keep exercising <see cref="LocalFileObjectStorage"/>
/// unchanged — the default provider never moves. GAP P1-6.
/// </summary>
[Collection(nameof(ObjectStorageS3Collection))]
public sealed class ObjectStorageS3Tests(ObjectStorageS3Fixture fixture)
{
    [Fact]
    public async Task SaveAsync_then_OpenReadAsync_round_trips_bytes_exactly()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 minio round-trip test bytes");
        using var content = new MemoryStream(bytes);

        var key = await fixture.Storage.SaveAsync(
            content, "application/pdf", bytes.Length, folder: "kyc", originalFileName: "test.pdf");

        Assert.StartsWith("kyc/", key);
        Assert.EndsWith(".pdf", key);

        await using var readStream = await fixture.Storage.OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await readStream.CopyToAsync(buffer);

        Assert.Equal(bytes, buffer.ToArray());
    }

    [Fact]
    public async Task GetReadableUrl_returns_presigned_url_downloadable_over_http()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 presigned url test bytes");
        using var content = new MemoryStream(bytes);
        var key = await fixture.Storage.SaveAsync(
            content, "application/pdf", bytes.Length, folder: "kyc", originalFileName: "presign.pdf");

        var url = fixture.Storage.GetReadableUrl(key);

        using var http = new HttpClient();
        var resp = await http.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var downloaded = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, downloaded);
    }

    [Fact]
    public async Task OpenReadAsync_missing_key_throws_FileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => fixture.Storage.OpenReadAsync($"kyc/does-not-exist-{Guid.NewGuid():N}.pdf"));
    }

    /// <summary>Same content-type/size guard as LocalFileObjectStorage — both share ObjectStorageRules.</summary>
    [Fact]
    public async Task SaveAsync_rejects_disallowed_content_type()
    {
        var bytes = Encoding.UTF8.GetBytes("not a pdf/image, just testing the guard");
        using var content = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Storage.SaveAsync(
                content, "application/x-msdownload", bytes.Length, folder: "kyc", originalFileName: "virus.exe"));
    }
}
