using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record UserAccountDeletedEvent(
    Guid UserId,
    DateTimeOffset DeletedAt) : DomainEvent;
