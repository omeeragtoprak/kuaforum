using Kuaforum.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Kuaforum.Infrastructure.Caching;

public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    public async Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString();
        var acquired = await db.StringSetAsync(key, token, duration, when: When.NotExists);
        return acquired ? new RedisLockHandle(db, key, token) : null;
    }

    private sealed class RedisLockHandle(IDatabase db, string key, string token) : IDistributedLockHandle
    {
        private const string ReleaseScript = @"
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end";

        public string Key => key;

        public async ValueTask DisposeAsync()
        {
            await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
        }
    }
}
