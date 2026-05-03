using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void Issue_NewToken_IsActive()
    {
        var token = RefreshToken.Issue(
            userId: Guid.NewGuid(),
            tokenHash: "hash-1",
            deviceFingerprint: "dev-1",
            deviceName: "iPhone",
            lifetime: TimeSpan.FromDays(30));

        Assert.True(token.IsActive);
        Assert.False(token.IsRevoked);
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void Rotate_SetsReplacedBy()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", "dev", null, TimeSpan.FromDays(30));
        var newId = Guid.NewGuid();

        var result = token.Rotate(newId, "127.0.0.1");
        Assert.True(result.IsSuccess);
        Assert.Equal(RefreshTokenRevokedReason.Rotated, token.RevokedReason);
        Assert.Equal(newId, token.ReplacedByTokenId);
        Assert.Equal("127.0.0.1", token.LastUsedIp);
    }

    [Fact]
    public void Rotate_AlreadyRevokedToken_Fails()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", "dev", null, TimeSpan.FromDays(30));
        token.Revoke(RefreshTokenRevokedReason.UserLogout);

        var result = token.Rotate(Guid.NewGuid(), null);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkReuseDetected_RaisesEvent()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", "dev", null, TimeSpan.FromDays(30));
        token.Revoke(RefreshTokenRevokedReason.Rotated);

        token.MarkReuseDetected("8.8.8.8");
        Assert.Equal(RefreshTokenRevokedReason.TokenReuse, token.RevokedReason);
        Assert.Single(token.DomainEvents.Where(e => e is RefreshTokenReuseDetectedEvent));
    }

    [Fact]
    public void Revoke_Idempotent()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", "dev", null, TimeSpan.FromDays(30));
        token.Revoke(RefreshTokenRevokedReason.UserLogout);
        var firstReason = token.RevokedReason;

        token.Revoke(RefreshTokenRevokedReason.AdminRevoke);
        Assert.Equal(firstReason, token.RevokedReason);
    }

    [Fact]
    public void IsExpired_AfterTtl()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", "dev", null, TimeSpan.FromMilliseconds(1));
        Thread.Sleep(20);

        Assert.True(token.IsExpired);
        Assert.False(token.IsActive);
    }
}
