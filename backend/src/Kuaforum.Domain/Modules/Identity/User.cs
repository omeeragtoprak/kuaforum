using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity.Events;
using Kuaforum.Domain.Modules.Identity.ValueObjects;

namespace Kuaforum.Domain.Modules.Identity;

public sealed class User : AggregateRoot
{
    private const int MaxDisplayNameLength = 80;
    private const int MaxAvatarUrlLength = 500;

    private User() { }

    public string? PhoneE164 { get; private set; }
    public string? Email { get; private set; }
    public string? EmailNormalized { get; private set; }
    public string? DisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string Locale { get; private set; } = "tr-TR";
    public string TimeZone { get; private set; } = "Europe/Istanbul";

    public bool IsDeleted => DeletedAt is not null || Status == UserStatus.Deleted;
    public bool CanLogin => Status == UserStatus.Active && !IsDeleted;

    public static Result<User> CreateCustomerByPhone(PhoneE164 phone, string? displayName = null)
    {
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        if (normalizedDisplayName.IsFailure)
            return Result.Failure<User>(normalizedDisplayName.Error);

        var user = new User
        {
            PhoneE164 = phone.Value,
            DisplayName = normalizedDisplayName.Value,
            Role = UserRole.Customer,
            Status = UserStatus.Active,
        };

        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, user.Role, "otp"));
        return user;
    }

    public static Result<User> CreateCustomerByOAuth(EmailAddress email, string registeredVia, string? displayName = null)
    {
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        if (normalizedDisplayName.IsFailure)
            return Result.Failure<User>(normalizedDisplayName.Error);

        var user = new User
        {
            Email = email.Value,
            EmailNormalized = email.Normalized,
            DisplayName = normalizedDisplayName.Value,
            Role = UserRole.Customer,
            Status = UserStatus.Active,
        };

        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, user.Role, registeredVia));
        return user;
    }

    public static Result<User> CreateAdmin(EmailAddress email, string displayName)
    {
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        if (normalizedDisplayName.IsFailure)
            return Result.Failure<User>(normalizedDisplayName.Error);

        var user = new User
        {
            Email = email.Value,
            EmailNormalized = email.Normalized,
            DisplayName = normalizedDisplayName.Value,
            Role = UserRole.Admin,
            Status = UserStatus.Active,
        };

        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, user.Role, "admin-seed"));
        return user;
    }

    public Result UpdateProfile(string? displayName, string? avatarUrl)
    {
        if (displayName is not null)
        {
            var normalized = NormalizeDisplayName(displayName);
            if (normalized.IsFailure)
                return Result.Failure(normalized.Error);
            DisplayName = normalized.Value;
        }

        if (avatarUrl is not null)
        {
            if (avatarUrl.Length > MaxAvatarUrlLength)
                return Result.Failure(Error.Validation($"Avatar URL must be at most {MaxAvatarUrlLength} chars."));
            AvatarUrl = avatarUrl;
        }

        Touch();
        return Result.Success();
    }

    public Result ChangePhone(PhoneE164 newPhone)
    {
        if (Status != UserStatus.Active)
            return Result.Failure(Error.Validation("Cannot change phone of non-active account."));

        PhoneE164 = newPhone.Value;
        Touch();
        return Result.Success();
    }

    public Result MarkLoggedIn(string deviceFingerprint, string? ip)
    {
        if (!CanLogin)
            return Result.Failure(Error.Forbidden("Account cannot login."));

        LastLoginAt = DateTimeOffset.UtcNow;
        Touch();
        RaiseDomainEvent(new UserLoggedInEvent(Id, deviceFingerprint, ip));
        return Result.Success();
    }

    public Result Suspend()
    {
        if (Status == UserStatus.Deleted)
            return Result.Failure(Error.Validation("Cannot suspend a deleted account."));
        if (Status == UserStatus.Suspended)
            return Result.Success();

        Status = UserStatus.Suspended;
        Touch();
        return Result.Success();
    }

    public Result Unsuspend()
    {
        if (Status != UserStatus.Suspended)
            return Result.Failure(Error.Validation("Account is not suspended."));

        Status = UserStatus.Active;
        Touch();
        return Result.Success();
    }

    public Result SoftDelete()
    {
        if (IsDeleted)
            return Result.Failure(Error.Validation("Account already deleted."));

        DeletedAt = DateTimeOffset.UtcNow;
        Status = UserStatus.Deleted;
        Touch();
        RaiseDomainEvent(new UserAccountDeletedEvent(Id, DeletedAt.Value));
        return Result.Success();
    }

    public Result RestoreFromSoftDelete()
    {
        if (DeletedAt is null)
            return Result.Failure(Error.Validation("Account is not in soft-delete state."));

        var elapsed = DateTimeOffset.UtcNow - DeletedAt.Value;
        if (elapsed > TimeSpan.FromDays(30))
            return Result.Failure(Error.Validation("Soft-delete window (30 days) has passed."));

        DeletedAt = null;
        Status = UserStatus.Active;
        Touch();
        RaiseDomainEvent(new UserAccountRestoredEvent(Id, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    public void AnonymizeForHardDelete()
    {
        PhoneE164 = "+900000000000";
        Email = null;
        EmailNormalized = null;
        DisplayName = "deleted-user";
        AvatarUrl = null;
        Touch();
    }

    private static Result<string?> NormalizeDisplayName(string? raw)
    {
        if (raw is null) return Result.Success<string?>(null);
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return Result.Success<string?>(null);
        if (trimmed.Length > MaxDisplayNameLength)
            return Result.Failure<string?>(Error.Validation($"DisplayName must be at most {MaxDisplayNameLength} chars."));
        return Result.Success<string?>(trimmed);
    }
}
