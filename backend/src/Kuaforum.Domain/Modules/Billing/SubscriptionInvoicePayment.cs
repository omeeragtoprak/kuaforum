using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Payment;

namespace Kuaforum.Domain.Modules.Billing;

public enum SubscriptionInvoicePaymentStatus
{
    Initiated = 0,
    Captured = 1,
    Failed = 2,
}

/// <summary>
/// RFC-012 madde 5b (RFC-014 ile SubscriptionInvoice'a taşındı) — PayTR online abonelik
/// ödemesi İSKELETİ. Sibling aggregate: Payment (RFC-006 booking/kapora akışı) invariantlarına
/// dokunmaz, yalnız IPaymentProvider arayüzünü (agregat-agnostik) paylaşır. Gerçek PSP
/// aktivasyonu founder'ın PayTR hesabına bloklu — bugün yalnız Mock provider ile initiate+capture
/// test edilir. Şema kesin DEĞİL (RFC'nin kendi ifadesiyle) — merchant hesabı bağlandığında ayrı
/// bir kısa implementasyon kararıyla netleşir.
/// </summary>
public sealed class SubscriptionInvoicePayment : AggregateRoot
{
    private SubscriptionInvoicePayment() { }

    public Guid BarberShopId { get; private set; }
    public Guid SubscriptionInvoiceId { get; private set; }
    public PaymentProviderName Provider { get; private set; }
    public SubscriptionInvoicePaymentStatus Status { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public static Result<SubscriptionInvoicePayment> Initiate(
        Guid barberShopId,
        Guid subscriptionInvoiceId,
        long amountMinor,
        PaymentProviderName provider,
        string? idempotencyKey = null)
    {
        if (amountMinor <= 0)
            return Result.Failure<SubscriptionInvoicePayment>(Error.Validation("Ödeme tutarı sıfırdan büyük olmalı."));

        return new SubscriptionInvoicePayment
        {
            BarberShopId = barberShopId,
            SubscriptionInvoiceId = subscriptionInvoiceId,
            AmountMinor = amountMinor,
            Provider = provider,
            Status = SubscriptionInvoicePaymentStatus.Initiated,
            IdempotencyKey = idempotencyKey,
        };
    }

    public Result Capture(string providerTransactionId)
    {
        if (Status != SubscriptionInvoicePaymentStatus.Initiated)
            return Result.Failure(Error.Validation($"{Status} durumundaki abonelik ödemesi tahsil edilemez."));
        if (string.IsNullOrWhiteSpace(providerTransactionId))
            return Result.Failure(Error.Validation("Sağlayıcı işlem numarası gerekli."));

        Status = SubscriptionInvoicePaymentStatus.Captured;
        ProviderTransactionId = providerTransactionId;
        CapturedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Fail(string reason)
    {
        if (Status == SubscriptionInvoicePaymentStatus.Captured)
            return Result.Failure(Error.Validation("Tahsil edilmiş abonelik ödemesi başarısız olarak işaretlenemez."));

        Status = SubscriptionInvoicePaymentStatus.Failed;
        FailureReason = reason;
        Touch();
        return Result.Success();
    }
}
