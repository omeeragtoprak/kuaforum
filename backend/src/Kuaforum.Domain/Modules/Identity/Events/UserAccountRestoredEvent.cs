using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record UserAccountRestoredEvent(
    Guid UserId,
    DateTimeOffset RestoredAt) : DomainEvent;
