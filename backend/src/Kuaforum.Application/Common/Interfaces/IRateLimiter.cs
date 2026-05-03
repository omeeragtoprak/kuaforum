namespace Kuaforum.Application.Common.Interfaces;

public sealed record RateLimitResult(bool Allowed, int RemainingQuota, TimeSpan? RetryAfter);

public interface IRateLimiter
{
    Task<RateLimitResult> CheckAsync(string bucket, int max, TimeSpan window, CancellationToken ct = default);
}
