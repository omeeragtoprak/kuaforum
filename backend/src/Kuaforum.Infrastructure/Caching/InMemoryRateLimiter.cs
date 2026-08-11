using System.Collections.Concurrent;
using System.Threading;
using Kuaforum.Application.Common.Interfaces;

namespace Kuaforum.Infrastructure.Caching;

/// <summary>
/// Redis'siz tek-örnekli (single-instance) dağıtımlar için <see cref="IRateLimiter"/> karşılığı —
/// process-local sabit pencere (fixed-window) sayaç. Redis'teki INCR + EXPIRE ile aynı fixed-window
/// semantiği: pencere süresi dolmuşsa sıfırla, dolmamışsa artır.
///
/// Sınır 1 (kabul edilebilir): SÜREÇ İÇİ — multi-instance dağıtımda örnekler arası paylaşılmaz,
/// yani her instance kendi sayacını tutar (Redis'in verdiği "global" garanti kaybolur). Koyeb
/// free-tier tek örnekli dağıtım için tasarlandı (bkz. InMemoryDistributedLock XML doc, aynı
/// gerekçe).
///
/// Sınır 2: Redis'in TTL'i olmadığı için süresi geçmiş sayaçlar kendiliğinden silinmez —
/// sözlük teorik olarak sınırsız büyüyebilir (örn. çok sayıda benzersiz telefon numarasıyla OTP
/// denemesi). Bunu önlemek için her ~2000 çağrıda bir, süresi uzun süre önce dolmuş girişleri
/// süpüren ucuz bir temizlik (<see cref="PruneExpired"/>) çalışır — kilitsiz, best-effort.
/// </summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private const int PruneEveryNCalls = 2000;
    private static readonly TimeSpan PruneStaleBuffer = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private long _callCount;

    public Task<RateLimitResult> CheckAsync(string bucket, int max, TimeSpan window, CancellationToken ct = default)
    {
        var counter = _counters.GetOrAdd(bucket, static _ => new Counter());
        var now = DateTimeOffset.UtcNow;

        int count;
        TimeSpan? retryAfter = null;
        lock (counter)
        {
            if (counter.WindowExpiresAt <= now)
            {
                counter.Count = 0;
                counter.WindowExpiresAt = now.Add(window);
            }

            counter.Count++;
            count = counter.Count;
            if (count > max)
                retryAfter = counter.WindowExpiresAt - now;
        }

        if (Interlocked.Increment(ref _callCount) % PruneEveryNCalls == 0)
            PruneExpired(now);

        var result = count > max
            ? new RateLimitResult(false, 0, retryAfter)
            : new RateLimitResult(true, max - count, null);
        return Task.FromResult(result);
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var cutoff = now - PruneStaleBuffer;
        foreach (var (key, counter) in _counters)
        {
            bool stale;
            lock (counter)
            {
                stale = counter.WindowExpiresAt <= cutoff;
            }

            if (stale)
                _counters.TryRemove(key, out _);
        }
    }

    private sealed class Counter
    {
        public int Count;
        public DateTimeOffset WindowExpiresAt;
    }
}
