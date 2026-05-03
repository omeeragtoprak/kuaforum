using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record ConsentRevokedEvent(
    Guid UserId,
    ConsentPurpose Purpose) : DomainEvent;
