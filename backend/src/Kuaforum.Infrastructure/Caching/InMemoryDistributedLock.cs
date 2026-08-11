using System.Collections.Concurrent;
using Kuaforum.Application.Common.Interfaces;

namespace Kuaforum.Infrastructure.Caching;

/// <summary>
/// Redis'siz tek-örnekli (single-instance) dağıtımlar için <see cref="IDistributedLock"/>
/// karşılığı — process-local <see cref="ConcurrentDictionary{TKey,TValue}"/> üzerinde atomik
/// TryAdd/TryUpdate ile Redis'in <c>SET key val NX PX</c> davranışını taklit eder.
///
/// Neden bu yeterli: Randevu slot çakışması ASIL PostgreSQL EXCLUDE constraint'inden gelir
/// (bkz. M15_BookingSlotExclusion migration'ı + <c>ApplicationDbContext</c> btree_gist
/// annotation'ı) — bu kilit yalnız "hızlı yol" optimizasyonu: DB'ye gitmeden aynı stylist için
/// eşzamanlı InitiateBooking/RescheduleBooking isteklerini erkenden ayırt eder
/// (bkz. InitiateBookingCommand/RescheduleBookingCommand XML yorumları).
///
/// Sınır: bu implementasyon SÜREÇ İÇİ (in-process) çalışır — birden fazla container/pod
/// (multi-instance/K8s) arasında paylaşılmaz. Koyeb free-tier tek örnekli (single-instance,
/// autoscale kapalı) bir dağıtım için tasarlandığından bu güvenli; dağıtım multi-instance'a
/// geçerse Redis (veya eşdeğeri) tekrar ZORUNLU hale gelir — bu sınıf o noktada sessizce yanlış
/// güvenlik hissi vermemeli (bkz. <c>RedisUnavailableStartupNotice</c> boot uyarısı).
/// </summary>
public sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, Entry> _locks = new();

    public Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid().ToString();
        var newEntry = new Entry(token, now.Add(duration));

        // Redis SET NX eşdeğeri: anahtar yoksa al. Anahtar varsa ama süresi dolmuşsa (stale —
        // örn. önceki sahip crash olup Dispose'a hiç ulaşamadı), atomik CAS ile devral.
        while (true)
        {
            if (_locks.TryAdd(key, newEntry))
                return Task.FromResult<IDistributedLockHandle?>(new Handle(this, key, token));

            if (_locks.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt > now)
                    return Task.FromResult<IDistributedLockHandle?>(null);

                if (_locks.TryUpdate(key, newEntry, existing))
                    return Task.FromResult<IDistributedLockHandle?>(new Handle(this, key, token));

                // Başka bir thread araya girdi (update veya remove) — tekrar dene.
                continue;
            }

            // TryAdd başarısız oldu ama TryGetValue anahtarı bulamadı — arada silinmiş,
            // baştan dene.
        }
    }

    private void Release(string key, string token)
    {
        if (_locks.TryGetValue(key, out var entry) && entry.Token == token)
            _locks.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }

    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);

    private sealed class Handle(InMemoryDistributedLock owner, string key, string token) : IDistributedLockHandle
    {
        public string Key => key;

        public ValueTask DisposeAsync()
        {
            owner.Release(key, token);
            return ValueTask.CompletedTask;
        }
    }
}
