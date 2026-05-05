using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber.Events;

public sealed record KycSubmittedEvent(Guid SubmissionId, Guid BarberShopId, Guid OwnerUserId) : DomainEvent;

public sealed record KycUnderReviewEvent(Guid SubmissionId, Guid ReviewerUserId) : DomainEvent;

public sealed record KycApprovedEvent(Guid SubmissionId, Guid BarberShopId, Guid OwnerUserId, Guid ReviewerUserId) : DomainEvent;

public sealed record KycRejectedEvent(Guid SubmissionId, Guid BarberShopId, Guid OwnerUserId, Guid ReviewerUserId, string Reason) : DomainEvent;
