using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record UserRegisteredEvent(
    Guid UserId,
    UserRole Role,
    string RegisteredVia) : DomainEvent;
