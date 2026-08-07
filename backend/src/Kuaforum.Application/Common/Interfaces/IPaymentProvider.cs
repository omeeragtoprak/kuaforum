using Kuaforum.Domain.Modules.Payment;

namespace Kuaforum.Application.Common.Interfaces;

/// <summary>
/// RFC-014 §2 D1: <see cref="CommissionMinor"/> platform pazaryeri split payı — RFC-012'nin ciro
/// komisyonu gelir modelinden RFC-014'ün sabit aylık aboneliğine geçişle her çağıran taraf
/// (InitiatePaymentCommand booking/kapora akışı, SubscriptionInvoicePaymentCommands abonelik
/// faturası online tahsilatı) bunu artık DAİMA 0 gönderiyor — platformun PSP split'inde payı
/// yok, tüm tutar kuaföre/sub-merchant'a akar. PSP split kodu (IPaymentProvider implementasyonu)
/// kasıtlı olarak SÖKÜLMEDİ, yalnız payı 0'a sabitlendi.
/// [unverified 2026-08-06 — PayTR'de 0 komisyonlu pazaryeri split'i (sub-merchant'a tam tutar
/// aktarımı) geçerli bir yapılandırma mı, merchant hesabı açılınca PayTR dokümantasyonu/destek
/// ile doğrulanacak — bkz. RFC-014.]
/// </summary>
public sealed record CheckoutRequest(
    Guid PaymentId,
    Guid BookingId,
    Guid CustomerId,
    string CustomerPhone,
    string? CustomerEmail,
    long GrossAmountMinor,
    long CommissionMinor,
    string SubMerchantKey,
    string Currency,
    string ReturnUrl,
    string CancelUrl);

public sealed record CheckoutSession(
    string CheckoutUrl,
    string? ProviderToken,
    DateTimeOffset ExpiresAt);

public sealed record PaymentVerification(
    bool Succeeded,
    string ProviderTransactionId,
    long PspMdrMinor,
    string? FailureReason);

public sealed record RefundRequest(
    string ProviderTransactionId,
    long AmountMinor,
    RefundReason Reason);

public sealed record RefundResult(
    bool Succeeded,
    string? ProviderRefundId,
    string? FailureReason);

public interface IPaymentProvider
{
    PaymentProviderName ProviderName { get; }

    Task<CheckoutSession> InitiateAsync(CheckoutRequest request, CancellationToken ct = default);
    Task<PaymentVerification> VerifyAsync(string providerToken, CancellationToken ct = default);
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct = default);
}

public interface IPaymentProviderRegistry
{
    IPaymentProvider GetProvider(PaymentProviderName name);
    PaymentProviderName DefaultProvider { get; }
}
