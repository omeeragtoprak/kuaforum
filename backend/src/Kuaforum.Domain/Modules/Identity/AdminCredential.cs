using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity;

public sealed class AdminCredential : Entity
{
    private AdminCredential() { }

    public Guid UserId { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public string TotpSecretEncrypted { get; private set; } = null!;
    public DateTimeOffset LastPasswordChangeAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public static AdminCredential Create(Guid userId, string passwordHash, string totpSecretEncrypted)
    {
        return new AdminCredential
        {
            Id = userId,
            UserId = userId,
            PasswordHash = passwordHash,
            TotpSecretEncrypted = totpSecretEncrypted,
            LastPasswordChangeAt = DateTimeOffset.UtcNow,
            FailedLoginAttempts = 0,
        };
    }

    public bool IsLocked() => LockedUntil is not null && LockedUntil > DateTimeOffset.UtcNow;

    public void RecordFailedAttempt()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= 5)
        {
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        Touch();
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        Touch();
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        LastPasswordChangeAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void RotateTotpSecret(string newEncryptedSecret)
    {
        TotpSecretEncrypted = newEncryptedSecret;
        Touch();
    }
}
