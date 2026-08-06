using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Booking.Events;

public sealed record BookingInitiatedEvent(Guid BookingId, Guid CustomerId, Guid BarberShopId, Guid StylistId, DateTimeOffset ScheduledAt, long TotalAmountMinor) : DomainEvent;

public sealed record BookingConfirmedEvent(Guid BookingId, Guid CustomerId, Guid StylistId, DateTimeOffset ScheduledAt) : DomainEvent;

/// <summary>RFC-013 C4: kapora/tam-ödeme capture edildi, booking Pending kalıyor — owner'a "ödenmiş talep onayınızı bekliyor" bildirimi bunu dinler.</summary>
public sealed record BookingPaymentReceivedEvent(Guid BookingId, Guid CustomerId, Guid StylistId, DateTimeOffset ScheduledAt) : DomainEvent;

public sealed record BookingStartedEvent(Guid BookingId, DateTimeOffset StartedAt) : DomainEvent;

public sealed record BookingCompletedEvent(Guid BookingId, Guid CustomerId, Guid StylistId, DateTimeOffset CompletedAt) : DomainEvent;

public sealed record BookingCancelledEvent(Guid BookingId, BookingCancellationReason Reason, int RefundPercentage) : DomainEvent;

public sealed record BookingNoShowEvent(Guid BookingId, Guid CustomerId) : DomainEvent;

public sealed record BookingRescheduledEvent(
    Guid BookingId,
    Guid CustomerId,
    Guid StylistId,
    DateTimeOffset OldScheduledAt,
    DateTimeOffset NewScheduledAt) : DomainEvent;
