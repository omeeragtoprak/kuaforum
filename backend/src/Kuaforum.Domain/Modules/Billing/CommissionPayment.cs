using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Payment;

namespace Kuaforum.Domain.Modules.Billing;

public enum CommissionPaymentStatus
{
    Initiated = 0,
    Captured = 1,
    Failed = 2,
}

/// <summary>
/// RFC-012 madde 5b — PayTR online komisyon ödemesi İSKELETİ. Sibling aggregate (RFC'nin
/// "Seçenek B" eğilimi): Payment (RFC-006 booking/kapora akışı) invariantlarına dokunmaz,
/// yalnız IPaymentProvider arayüzünü (agregat-agnostik) paylaşır. Gerçek PSP aktivasyonu
/// founder'ın PayTR hesabına bloklu — bugün yalnız Mock provider ile initiate+capture test edilir.
/// Şema kesin DEĞİL (RFC'nin kendi ifadesiyle) — merchant hesabı bağlandığında ayrı bir kısa
/// implementasyon kararıyla netleşir.
/// </summary>
public sealed class CommissionPayment : AggregateRoot
{
    private CommissionPayment() { }

    public Guid BarberShopId { get; private set; }
    public Guid CommissionStatementId { get; private set; }
    public PaymentProviderName Provider { get; private set; }
    public CommissionPaymentStatus Status { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public static Result<CommissionPayment> Initiate(
        Guid barberShopId,
        Guid commissionStatementId,
        long amountMinor,
        PaymentProviderName provider,
        string? idempotencyKey = null)
    {
        if (amountMinor <= 0)
            return Result.Failure<CommissionPayment>(Error.Validation("Ödeme tutarı sıfırdan büyük olmalı."));

        return new CommissionPayment
        {
            BarberShopId = barberShopId,
            CommissionStatementId = commissionStatementId,
            AmountMinor = amountMinor,
            Provider = provider,
            Status = CommissionPaymentStatus.Initiated,
            IdempotencyKey = idempotencyKey,
        };
    }

    public Result Capture(string providerTransactionId)
    {
        if (Status != CommissionPaymentStatus.Initiated)
            return Result.Failure(Error.Validation($"{Status} durumundaki komisyon ödemesi tahsil edilemez."));
        if (string.IsNullOrWhiteSpace(providerTransactionId))
            return Result.Failure(Error.Validation("Sağlayıcı işlem numarası gerekli."));

        Status = CommissionPaymentStatus.Captured;
        ProviderTransactionId = providerTransactionId;
        CapturedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Fail(string reason)
    {
        if (Status == CommissionPaymentStatus.Captured)
            return Result.Failure(Error.Validation("Tahsil edilmiş komisyon ödemesi başarısız olarak işaretlenemez."));

        Status = CommissionPaymentStatus.Failed;
        FailureReason = reason;
        Touch();
        return Result.Success();
    }
}
