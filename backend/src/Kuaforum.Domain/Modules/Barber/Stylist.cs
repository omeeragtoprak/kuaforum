using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber;

public sealed class Stylist : Entity
{
    private Stylist() { }

    public Guid BarberShopId { get; private set; }
    public Guid? UserId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string? PhotoUrl { get; private set; }
    public string? Bio { get; private set; }
    public bool IsActive { get; private set; }
    public bool WalkInAvailable { get; private set; }
    public DateTimeOffset? WalkInToggledAt { get; private set; }

    public static Result<Stylist> Create(Guid barberShopId, string displayName, Guid? userId = null)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80)
            return Result.Failure<Stylist>(Error.Validation("DisplayName max 80 chars."));

        return new Stylist
        {
            BarberShopId = barberShopId,
            UserId = userId,
            DisplayName = displayName.Trim(),
            IsActive = true,
            WalkInAvailable = false,
        };
    }

    public Result UpdateProfile(string? displayName, string? photoUrl, string? bio)
    {
        if (displayName is not null)
        {
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80)
                return Result.Failure(Error.Validation("DisplayName max 80."));
            DisplayName = displayName.Trim();
        }
        if (photoUrl is not null) PhotoUrl = photoUrl.Length > 500 ? photoUrl[..500] : photoUrl;
        if (bio is not null) Bio = bio.Length > 1000 ? bio[..1000] : bio;
        Touch();
        return Result.Success();
    }

    public Result SetActive(bool isActive)
    {
        IsActive = isActive;
        if (!isActive) WalkInAvailable = false;
        Touch();
        return Result.Success();
    }

    public Result ToggleWalkIn(bool available)
    {
        if (!IsActive && available)
            return Result.Failure(Error.Validation("Inactive stylist cannot enable walk-in."));
        WalkInAvailable = available;
        WalkInToggledAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public bool ShouldExpireWalkIn(TimeSpan maxAge)
        => WalkInAvailable && WalkInToggledAt is not null && DateTimeOffset.UtcNow - WalkInToggledAt > maxAge;
}
