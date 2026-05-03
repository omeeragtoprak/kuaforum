using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record UserLoggedInEvent(
    Guid UserId,
    string DeviceFingerprint,
    string? Ip) : DomainEvent;
