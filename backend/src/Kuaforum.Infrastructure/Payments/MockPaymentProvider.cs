using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Payment;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Payments;

/// <summary>
/// Development / demo payment provider. Auto-succeeds without any real PSP call.
/// Replace with PayTrPaymentProvider in staging/production.
/// </summary>
public sealed class MockPaymentProvider(ILogger<MockPaymentProvider> logger) : IPaymentProvider
{
    public PaymentProviderName ProviderName => PaymentProviderName.Mock;

    public Task<CheckoutSession> InitiateAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        logger.LogWarning("MOCK PAYMENT: initiated for booking {BookingId}, amount {Amount} kuruş.", request.BookingId, request.GrossAmountMinor);
        var token = $"mock-{request.PaymentId:N}";
        // Relative URL bilinçli: mobile client (payment.tsx toRelativePath) origin'i zaten
        // soyup kendi apiClient baseURL'ine istek atıyor. Sabit bir origin (örn. localhost:5000)
        // hem gerçek dev portuyla (5100) çelişiyordu hem de Android emülatörde erişilemezdi.
        return Task.FromResult(new CheckoutSession(
            CheckoutUrl: $"/v1/payments/mock/complete?token={token}",
            ProviderToken: token,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30)));
    }

    public Task<PaymentVerification> VerifyAsync(string providerToken, CancellationToken ct = default)
    {
        logger.LogWarning("MOCK PAYMENT: auto-verifying token {Token}.", providerToken);
        // Mock: %2.99 fake MDR
        return Task.FromResult(new PaymentVerification(
            Succeeded: true,
            ProviderTransactionId: $"mock-tx-{Guid.NewGuid():N}",
            PspMdrMinor: 0, // mock — no fee
            FailureReason: null));
    }

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct = default)
    {
        logger.LogWarning("MOCK PAYMENT: auto-refunding tx {TxId} amount {Amount}.", request.ProviderTransactionId, request.AmountMinor);
        return Task.FromResult(new RefundResult(
            Succeeded: true,
            ProviderRefundId: $"mock-refund-{Guid.NewGuid():N}",
            FailureReason: null));
    }
}
