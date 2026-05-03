using Kuaforum.Domain.Modules.Payment;

namespace Kuaforum.Application.Common.Interfaces;

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
