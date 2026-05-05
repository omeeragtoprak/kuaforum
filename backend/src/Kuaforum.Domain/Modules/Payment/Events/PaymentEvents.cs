using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Payment.Events;

public sealed record PaymentInitiatedEvent(Guid PaymentId, Guid BookingId, Guid CustomerId, long AmountMinor, PaymentProviderName Provider) : DomainEvent;

public sealed record PaymentCapturedEvent(Guid PaymentId, Guid BookingId, long AmountMinor, string ProviderTransactionId) : DomainEvent;

public sealed record PaymentFailedEvent(Guid PaymentId, Guid BookingId, string Reason) : DomainEvent;

public sealed record PaymentRefundedEvent(Guid PaymentId, Guid RefundId, long AmountMinor, RefundReason Reason) : DomainEvent;

public sealed record PaymentAuthorizedEvent(Guid PaymentId, Guid BookingId, string ProviderTransactionId) : DomainEvent;

public sealed record PaymentSettledEvent(Guid PaymentId, Guid BookingId, long NetToBarberMinor, DateTimeOffset SettledAt) : DomainEvent;
