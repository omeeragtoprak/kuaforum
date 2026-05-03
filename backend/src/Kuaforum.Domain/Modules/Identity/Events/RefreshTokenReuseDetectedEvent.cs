using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record RefreshTokenReuseDetectedEvent(
    Guid UserId,
    Guid RefreshTokenId,
    string? Ip) : DomainEvent;
