using Kuaforum.Infrastructure.Caching;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Koyeb free-tier deploy hazırlığı (2026-08): Redis'siz fallback'lerin (bkz.
/// DependencyInjection.AddInfrastructure) kendi başına doğru davrandığını, DB/Testcontainers
/// olmadan, saf birim test seviyesinde kanıtlar. Slot-çakışma/rate-limit uçtan uca kanıtı
/// <see cref="NoRedisIntegrationTests"/>'te (gerçek HTTP + Postgres üzerinden).
/// </summary>
public sealed class InMemoryCachingFallbackTests
{
    [Fact]
    public async Task DistributedLock_second_acquire_on_same_key_fails_while_first_is_held()
    {
        var sut = new InMemoryDistributedLock();

        await using var first = await sut.TryAcquireAsync("k1", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        var second = await sut.TryAcquireAsync("k1", TimeSpan.FromSeconds(30));
        Assert.Null(second);
    }

    [Fact]
    public async Task DistributedLock_key_is_reacquirable_after_dispose()
    {
        var sut = new InMemoryDistributedLock();

        var first = await sut.TryAcquireAsync("k2", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        await first!.DisposeAsync();

        var second = await sut.TryAcquireAsync("k2", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task DistributedLock_stale_expired_entry_is_reclaimed_without_dispose()
    {
        var sut = new InMemoryDistributedLock();

        // Duration=0 → immediately expired, simulates a holder that crashed before disposing.
        var first = await sut.TryAcquireAsync("k3", TimeSpan.Zero);
        Assert.NotNull(first);

        var second = await sut.TryAcquireAsync("k3", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task DistributedLock_different_keys_do_not_contend()
    {
        var sut = new InMemoryDistributedLock();

        await using var a = await sut.TryAcquireAsync("bucket-a", TimeSpan.FromSeconds(30));
        await using var b = await sut.TryAcquireAsync("bucket-b", TimeSpan.FromSeconds(30));

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task RateLimiter_allows_up_to_max_then_denies()
    {
        var sut = new InMemoryRateLimiter();

        var r1 = await sut.CheckAsync("phone:+905551234567", max: 3, window: TimeSpan.FromMinutes(1));
        var r2 = await sut.CheckAsync("phone:+905551234567", max: 3, window: TimeSpan.FromMinutes(1));
        var r3 = await sut.CheckAsync("phone:+905551234567", max: 3, window: TimeSpan.FromMinutes(1));
        var r4 = await sut.CheckAsync("phone:+905551234567", max: 3, window: TimeSpan.FromMinutes(1));

        Assert.True(r1.Allowed);
        Assert.True(r2.Allowed);
        Assert.True(r3.Allowed);
        Assert.False(r4.Allowed);
        Assert.NotNull(r4.RetryAfter);
    }

    [Fact]
    public async Task RateLimiter_different_buckets_have_independent_quotas()
    {
        var sut = new InMemoryRateLimiter();

        var a1 = await sut.CheckAsync("phone:+905550000001", max: 1, window: TimeSpan.FromMinutes(1));
        var a2 = await sut.CheckAsync("phone:+905550000001", max: 1, window: TimeSpan.FromMinutes(1));
        var b1 = await sut.CheckAsync("phone:+905550000002", max: 1, window: TimeSpan.FromMinutes(1));

        Assert.True(a1.Allowed);
        Assert.False(a2.Allowed); // same bucket, over quota
        Assert.True(b1.Allowed); // different bucket, unaffected
    }

    [Fact]
    public async Task RateLimiter_resets_after_window_expires()
    {
        var sut = new InMemoryRateLimiter();

        var r1 = await sut.CheckAsync("burst-key", max: 1, window: TimeSpan.FromMilliseconds(50));
        Assert.True(r1.Allowed);

        var r2 = await sut.CheckAsync("burst-key", max: 1, window: TimeSpan.FromMilliseconds(50));
        Assert.False(r2.Allowed);

        await Task.Delay(150);

        var r3 = await sut.CheckAsync("burst-key", max: 1, window: TimeSpan.FromMilliseconds(50));
        Assert.True(r3.Allowed, "Pencere süresi dolduktan sonra sayaç sıfırlanıp yeni istek kabul edilmeli.");
    }
}
