using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity;

public enum DevicePlatform
{
    Ios = 0,
    Android = 1,
    Web = 2,
}

/// <summary>
/// Push delivery target. One row per (user, fcm token).
/// Token rotates frequently; mobile re-registers on app start.
/// </summary>
public sealed class DeviceToken : Entity
{
    private DeviceToken() { }

    public Guid UserId { get; private set; }
    public string FcmToken { get; private set; } = null!;
    public DevicePlatform Platform { get; private set; }
    public string? AppVersion { get; private set; }
    public string? DeviceModel { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public bool IsActive { get; private set; }

    public static DeviceToken Register(
        Guid userId,
        string fcmToken,
        DevicePlatform platform,
        string? appVersion,
        string? deviceModel)
    {
        return new DeviceToken
        {
            UserId = userId,
            FcmToken = fcmToken,
            Platform = platform,
            AppVersion = appVersion,
            DeviceModel = deviceModel,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
    }

    public void Refresh(string? appVersion, string? deviceModel)
    {
        AppVersion = appVersion ?? AppVersion;
        DeviceModel = deviceModel ?? DeviceModel;
        LastSeenAt = DateTimeOffset.UtcNow;
        IsActive = true;
        Touch();
    }

    public void Deactivate(string? reason = null)
    {
        if (!IsActive) return;
        IsActive = false;
        Touch();
    }

    public void ReassignTo(Guid newUserId, string? appVersion, string? deviceModel)
    {
        UserId = newUserId;
        AppVersion = appVersion ?? AppVersion;
        DeviceModel = deviceModel ?? DeviceModel;
        LastSeenAt = DateTimeOffset.UtcNow;
        IsActive = true;
        Touch();
    }
}
