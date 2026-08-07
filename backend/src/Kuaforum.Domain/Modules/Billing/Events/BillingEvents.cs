using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing.Events;

public sealed record SubscriptionInvoiceNotifiedEvent(
    Guid InvoiceId,
    Guid BarberShopId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long AmountMinor,
    DateOnly DueDate) : DomainEvent;

public sealed record SubscriptionInvoiceSuspendedEvent(
    Guid InvoiceId,
    Guid BarberShopId,
    long AmountMinor) : DomainEvent;
