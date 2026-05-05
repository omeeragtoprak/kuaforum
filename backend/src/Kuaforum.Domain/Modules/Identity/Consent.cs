using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity.Events;

namespace Kuaforum.Domain.Modules.Identity;

public sealed class Consent : Entity
{
    private Consent() { }

    public Guid UserId { get; private set; }
    public ConsentPurpose Purpose { get; private set; }
    public bool Granted { get; private set; }
    public string Version { get; private set; } = null!;
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public ConsentSource Source { get; private set; }

    public static Consent Grant(Guid userId, ConsentPurpose purpose, string version, ConsentSource source)
    {
        var consent = new Consent
        {
            UserId = userId,
            Purpose = purpose,
            Granted = true,
            Version = version,
            GrantedAt = DateTimeOffset.UtcNow,
            Source = source,
        };
        consent.RaiseDomainEvent(new ConsentGrantedEvent(userId, purpose, version));
        return consent;
    }

    public Result Revoke()
    {
        if (!Granted || RevokedAt is not null)
            return Result.Failure(Error.Validation("Bu rıza zaten geçerli değil."));

        Granted = false;
        RevokedAt = DateTimeOffset.UtcNow;
        Touch();
        RaiseDomainEvent(new ConsentRevokedEvent(UserId, Purpose));
        return Result.Success();
    }
}

public enum ConsentPurpose
{
    TermsOfService = 0,
    Marketing = 1,
    Location = 2,
    HealthData = 3,
}

public enum ConsentSource
{
    Registration = 0,
    SettingsUpdate = 1,
    RevocationRequest = 2,
}
