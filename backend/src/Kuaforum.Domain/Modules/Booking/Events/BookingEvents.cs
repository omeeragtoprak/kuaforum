using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Booking.Events;

public sealed record BookingInitiatedEvent(Guid BookingId, Guid CustomerId, Guid BarberShopId, Guid StylistId, DateTimeOffset ScheduledAt, long TotalAmountMinor) : DomainEvent;

/// <summary>
/// RFC-014 §2 D2: <c>AutoConfirmed</c> ayırt eder — owner'ın kendi <c>OwnerConfirm()</c>
/// çağrısıyla mı (false) yoksa salonun <c>BookingPolicy.AutoConfirmEnabled</c> ayarıyla mı
/// (true, <see cref="Kuaforum.Domain.Modules.Booking.Booking.Initiate"/> veya
/// <see cref="Kuaforum.Domain.Modules.Booking.Booking.MarkPaymentReceived"/> üzerinden) Confirmed'a
/// geçildiği — bildirim metni bu alana göre dallanır ("Yeni randevu" vs. "Yeni randevu —
/// otomatik onaylandı").
/// </summary>
public sealed record BookingConfirmedEvent(Guid BookingId, Guid CustomerId, Guid StylistId, DateTimeOffset ScheduledAt, bool AutoConfirmed) : DomainEvent;

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
