using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing.Events;

public sealed record CommissionStatementNotifiedEvent(
    Guid StatementId,
    Guid BarberShopId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long AmountDueMinor,
    DateOnly DueDate) : DomainEvent;

public sealed record CommissionStatementSuspendedEvent(
    Guid StatementId,
    Guid BarberShopId,
    long AmountDueMinor) : DomainEvent;
