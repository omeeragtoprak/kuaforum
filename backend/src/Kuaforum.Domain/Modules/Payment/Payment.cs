using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Payment.Events;

namespace Kuaforum.Domain.Modules.Payment;

public sealed class Payment : AggregateRoot
{
    private readonly List<Refund> _refunds = new();

    private Payment() { }

    public Guid BookingId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SubMerchantBarberShopId { get; private set; }
    public PaymentProviderName Provider { get; private set; }
    public PaymentStatus Status { get; private set; }

    public long GrossAmountMinor { get; private set; }       // total customer paid (TL minor)
    public long PlatformCommissionMinor { get; private set; }
    public long PspMdrMinor { get; private set; }            // PSP fee
    public long NetToBarberMinor { get; private set; }       // gross - commission - mdr
    public string Currency { get; private set; } = "TRY";

    public string? ProviderTransactionId { get; private set; }
    public string? ProviderPaymentToken { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public static Result<Payment> Initiate(
        Guid bookingId,
        Guid customerId,
        Guid subMerchantBarberShopId,
        long grossAmountMinor,
        long platformCommissionMinor,
        PaymentProviderName provider,
        string? idempotencyKey = null)
    {
        if (grossAmountMinor <= 0)
            return Result.Failure<Payment>(Error.Validation("Gross amount must be positive."));
        if (platformCommissionMinor < 0 || platformCommissionMinor > grossAmountMinor)
            return Result.Failure<Payment>(Error.Validation("Commission out of range."));

        var payment = new Payment
        {
            BookingId = bookingId,
            CustomerId = customerId,
            SubMerchantBarberShopId = subMerchantBarberShopId,
            Provider = provider,
            Status = PaymentStatus.Initiated,
            GrossAmountMinor = grossAmountMinor,
            PlatformCommissionMinor = platformCommissionMinor,
            IdempotencyKey = idempotencyKey,
        };

        payment.RaiseDomainEvent(new PaymentInitiatedEvent(
            payment.Id, bookingId, customerId, grossAmountMinor, provider));
        return payment;
    }

    public Result Capture(string providerTransactionId, long pspMdrMinor)
    {
        if (Status != PaymentStatus.Initiated && Status != PaymentStatus.Authorized)
            return Result.Failure(Error.Validation($"Cannot capture in status {Status}."));
        if (pspMdrMinor < 0)
            return Result.Failure(Error.Validation("PSP MDR cannot be negative."));

        Status = PaymentStatus.Captured;
        ProviderTransactionId = providerTransactionId;
        PspMdrMinor = pspMdrMinor;
        NetToBarberMinor = GrossAmountMinor - PlatformCommissionMinor - pspMdrMinor;
        CapturedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new PaymentCapturedEvent(Id, BookingId, GrossAmountMinor, providerTransactionId));
        return Result.Success();
    }

    public Result MarkSettled()
    {
        if (Status != PaymentStatus.Captured)
            return Result.Failure(Error.Validation($"Cannot settle in status {Status}."));

        Status = PaymentStatus.Settled;
        SettledAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Fail(string reason)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Settled or PaymentStatus.Refunded)
            return Result.Failure(Error.Validation($"Cannot fail in status {Status}."));

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        Touch();

        RaiseDomainEvent(new PaymentFailedEvent(Id, BookingId, reason));
        return Result.Success();
    }

    public Result<Refund> AddRefund(long amountMinor, RefundReason reason, string? providerRefundId = null)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.Settled && Status != PaymentStatus.PartiallyRefunded)
            return Result.Failure<Refund>(Error.Validation($"Cannot refund in status {Status}."));
        if (amountMinor <= 0)
            return Result.Failure<Refund>(Error.Validation("Refund amount must be positive."));

        var alreadyRefunded = _refunds.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.AmountMinor);
        if (alreadyRefunded + amountMinor > GrossAmountMinor)
            return Result.Failure<Refund>(Error.Validation("Refund exceeds remaining payment amount."));

        var refund = Refund.Create(Id, amountMinor, reason, providerRefundId);
        _refunds.Add(refund);

        var totalRefunded = alreadyRefunded + amountMinor;
        Status = totalRefunded >= GrossAmountMinor ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();

        RaiseDomainEvent(new PaymentRefundedEvent(Id, refund.Id, amountMinor, reason));
        return refund;
    }
}

public sealed class Refund : Entity
{
    private Refund() { }

    public Guid PaymentId { get; private set; }
    public long AmountMinor { get; private set; }
    public RefundReason Reason { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static Refund Create(Guid paymentId, long amountMinor, RefundReason reason, string? providerRefundId = null)
    {
        return new Refund
        {
            PaymentId = paymentId,
            AmountMinor = amountMinor,
            Reason = reason,
            Status = RefundStatus.Succeeded, // Mock provider auto-success; real PSP will start as Pending
            ProviderRefundId = providerRefundId,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }
}

public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}
