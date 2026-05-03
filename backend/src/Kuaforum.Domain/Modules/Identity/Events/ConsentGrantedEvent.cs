using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.Events;

public sealed record ConsentGrantedEvent(
    Guid UserId,
    ConsentPurpose Purpose,
    string Version) : DomainEvent;
