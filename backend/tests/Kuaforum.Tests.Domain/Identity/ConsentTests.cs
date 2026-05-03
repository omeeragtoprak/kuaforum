using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Identity;

public class ConsentTests
{
    [Fact]
    public void Grant_RaisesEvent()
    {
        var userId = Guid.NewGuid();
        var consent = Consent.Grant(userId, ConsentPurpose.Marketing, "v1", ConsentSource.Registration);

        Assert.True(consent.Granted);
        Assert.Equal("v1", consent.Version);
        Assert.Single(consent.DomainEvents.Where(e => e is ConsentGrantedEvent));
    }

    [Fact]
    public void Revoke_DeactivatesAndRaisesEvent()
    {
        var consent = Consent.Grant(Guid.NewGuid(), ConsentPurpose.Marketing, "v1", ConsentSource.Registration);
        consent.ClearDomainEvents();

        var result = consent.Revoke();
        Assert.True(result.IsSuccess);
        Assert.False(consent.Granted);
        Assert.NotNull(consent.RevokedAt);
        Assert.Single(consent.DomainEvents.Where(e => e is ConsentRevokedEvent));
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Fails()
    {
        var consent = Consent.Grant(Guid.NewGuid(), ConsentPurpose.Marketing, "v1", ConsentSource.Registration);
        consent.Revoke();

        var second = consent.Revoke();
        Assert.True(second.IsFailure);
    }
}
