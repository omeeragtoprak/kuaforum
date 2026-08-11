using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Caching;

/// <summary>
/// <c>ConnectionStrings:Redis</c> yapılandırılmadığında (Koyeb free-tier gibi Redis'siz
/// dağıtımlarda) DI, rate limiting ve slot kilidini in-memory karşılıklara düşürür (bkz.
/// <see cref="InMemoryRateLimiter"/>, <see cref="InMemoryDistributedLock"/>). Bu düşüş
/// SESSİZ olmamalı — hangi korumanın tek-örnekli moda geçtiği boot log'unda BİR KEZ görünür
/// olsun diye bu <see cref="IHostedService"/> yalnız Redis eksikken DI'a eklenir (bkz.
/// DependencyInjection.AddInfrastructure).
/// </summary>
public sealed class RedisUnavailableStartupNotice(ILogger<RedisUnavailableStartupNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "ConnectionStrings:Redis yapılandırılmadı — rate limiting bellek-içi sayaca, " +
            "randevu slot kilidi bellek-içi kilide düştü. Tek-örnekli (single-instance) " +
            "dağıtımda güvenli bir davranış (randevu çakışması garantisi zaten PostgreSQL " +
            "EXCLUDE constraint'inden gelir — M15_BookingSlotExclusion); ÇOK ÖRNEKLİ " +
            "(multi-instance/K8s) bir dağıtımda rate limit ve kilit örnekler arası " +
            "PAYLAŞILMAZ — o durumda Redis (veya eşdeğeri) tekrar ZORUNLUDUR.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
