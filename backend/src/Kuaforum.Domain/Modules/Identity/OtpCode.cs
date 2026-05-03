using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity;

public sealed class OtpCode : Entity
{
    public const int DefaultAttemptsAllowed = 5;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private OtpCode() { }

    public string PhoneE164 { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public OtpPurpose Purpose { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int AttemptsLeft { get; private set; }
    public OtpCodeStatus Status { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsActive => Status == OtpCodeStatus.Pending && DateTimeOffset.UtcNow < ExpiresAt;

    public static OtpCode Issue(
        string phoneE164,
        string codeHash,
        OtpPurpose purpose,
        TimeSpan? lifetime = null,
        int? attemptsAllowed = null)
    {
        return new OtpCode
        {
            PhoneE164 = phoneE164,
            CodeHash = codeHash,
            Purpose = purpose,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultLifetime),
            AttemptsLeft = attemptsAllowed ?? DefaultAttemptsAllowed,
            Status = OtpCodeStatus.Pending,
        };
    }

    public Result<bool> AttemptVerify(string candidateCodeHash)
    {
        if (!IsActive)
        {
            ExpireIfApplicable();
            return Result.Failure<bool>(Error.Validation("OTP is no longer active."));
        }

        if (CodeHash == candidateCodeHash)
        {
            Status = OtpCodeStatus.Consumed;
            UsedAt = DateTimeOffset.UtcNow;
            Touch();
            return Result.Success(true);
        }

        AttemptsLeft--;
        if (AttemptsLeft <= 0)
        {
            Status = OtpCodeStatus.Expired;
        }
        Touch();
        return Result.Success(false);
    }

    private void ExpireIfApplicable()
    {
        if (Status == OtpCodeStatus.Pending && DateTimeOffset.UtcNow >= ExpiresAt)
        {
            Status = OtpCodeStatus.Expired;
            Touch();
        }
    }
}

public enum OtpPurpose
{
    Register = 0,
    Login = 1,
    PhoneChange = 2,
    PasswordReset = 3,
}

public enum OtpCodeStatus
{
    Pending = 0,
    Consumed = 1,
    Expired = 2,
}
