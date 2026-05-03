using Kuaforum.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Kuaforum.Infrastructure.Caching;

public sealed class RedisRateLimiter(IConnectionMultiplexer redis) : IRateLimiter
{
    private const string KeyPrefix = "rl:";

    public async Task<RateLimitResult> CheckAsync(
        string bucket,
        int max,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = $"{KeyPrefix}{bucket}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, window);
        }

        if (count > max)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            return new RateLimitResult(false, 0, ttl);
        }

        return new RateLimitResult(true, max - (int)count, null);
    }
}
