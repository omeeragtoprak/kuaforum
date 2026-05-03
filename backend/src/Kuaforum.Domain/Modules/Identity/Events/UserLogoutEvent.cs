using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record UserLogoutEvent(
    Guid UserId,
    Guid RefreshTokenId) : DomainEvent;
