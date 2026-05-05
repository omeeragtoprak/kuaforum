using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Review.Events;

public sealed record ReviewSubmittedEvent(Guid ReviewId, Guid CustomerId, Guid BarberShopId, Guid StylistId, Guid BookingId, int Rating) : DomainEvent;

public sealed record ReviewApprovedEvent(Guid ReviewId, Guid CustomerId, Guid BarberShopId, Guid StylistId, int Rating) : DomainEvent;

public sealed record ReviewRejectedEvent(Guid ReviewId, ReviewRejectionReason Reason) : DomainEvent;

public sealed record ReviewReplyAddedEvent(Guid ReviewId, Guid BarberOwnerId) : DomainEvent;

public sealed record ReviewHiddenEvent(Guid ReviewId, Guid ModeratorUserId) : DomainEvent;
