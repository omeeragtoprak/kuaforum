using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity.Events;

namespace Kuaforum.Domain.Modules.Identity;

public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string DeviceFingerprint { get; private set; } = null!;
    public string? DeviceName { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevokedReason? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public string? LastUsedIp { get; private set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired => RevokedAt is null && ExpiresAt <= DateTimeOffset.UtcNow;

    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        string deviceFingerprint,
        string? deviceName,
        TimeSpan lifetime)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            DeviceFingerprint = deviceFingerprint,
            DeviceName = deviceName,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
        };
    }

    public Result Rotate(Guid replacementTokenId, string? ip)
    {
        if (!IsActive)
            return Result.Failure(Error.Validation("Token is not active and cannot be rotated."));

        ReplacedByTokenId = replacementTokenId;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedReason = RefreshTokenRevokedReason.Rotated;
        LastUsedAt = DateTimeOffset.UtcNow;
        LastUsedIp = ip;
        Touch();
        return Result.Success();
    }

    public Result Revoke(RefreshTokenRevokedReason reason)
    {
        if (IsRevoked)
            return Result.Success();

        RevokedAt = DateTimeOffset.UtcNow;
        RevokedReason = reason;
        Touch();
        return Result.Success();
    }

    public Result MarkReuseDetected(string? ip)
    {
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedReason = RefreshTokenRevokedReason.TokenReuse;
        LastUsedIp = ip;
        Touch();
        RaiseDomainEvent(new RefreshTokenReuseDetectedEvent(UserId, Id, ip));
        return Result.Success();
    }

    public void RecordUsage(string? ip)
    {
        LastUsedAt = DateTimeOffset.UtcNow;
        LastUsedIp = ip;
        Touch();
    }
}

public enum RefreshTokenRevokedReason
{
    UserLogout = 0,
    Rotated = 1,
    TokenReuse = 2,
    AdminRevoke = 3,
    AccountDeleted = 4,
    Expired = 5,
    DeviceLimitExceeded = 6,
}
