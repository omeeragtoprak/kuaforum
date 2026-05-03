using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Payments;

/// <summary>
/// PayTR Pazaryeri PSP provider. Production endpoint: https://www.paytr.com/odeme/api
/// Sandbox: PayTR test merchant credentials (request from PayTR team).
///
/// IMPORTANT: This is a SCAFFOLDED stub. Real implementation requires:
/// 1. PayTR merchant account + API credentials (merchantId, merchantKey, merchantSalt)
/// 2. Sub-merchant onboarding flow per BarberShop (KYC at PayTR)
/// 3. iframe / redirect-based checkout per PayTR docs
/// 4. HMAC signature on requests + verify on webhooks
/// 5. Sandbox testing before going live
///
/// See docs/01-research/payment-infrastructure.md for full integration plan.
/// </summary>
public sealed class PayTrPaymentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<PayTrOptions> options,
    ILogger<PayTrPaymentProvider> logger) : IPaymentProvider
{
    public PaymentProviderName ProviderName => PaymentProviderName.PayTr;

    private readonly PayTrOptions _options = options.Value;

    public async Task<CheckoutSession> InitiateAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.MerchantId))
            throw new InvalidOperationException("PayTR not configured. Set PayTr:MerchantId/MerchantKey/MerchantSalt or use Mock provider.");

        // TODO(M7-impl): build PayTR iframe POST body, HMAC-sign, POST to /odeme/api/get-token
        // Reference: https://dev.paytr.com/iframe-api/iframe-api-1-adim
        logger.LogWarning("PayTrPaymentProvider.InitiateAsync — STUB (real impl pending). Returning placeholder.");

        await Task.CompletedTask;
        return new CheckoutSession(
            CheckoutUrl: "https://www.paytr.com/odeme/guvenli/PLACEHOLDER",
            ProviderToken: $"paytr-placeholder-{request.PaymentId:N}",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
    }

    public Task<PaymentVerification> VerifyAsync(string providerToken, CancellationToken ct = default)
    {
        // TODO(M7-impl): verify HMAC of webhook payload, parse status, return result
        logger.LogWarning("PayTrPaymentProvider.VerifyAsync — STUB (real impl pending).");
        return Task.FromResult(new PaymentVerification(
            Succeeded: false,
            ProviderTransactionId: string.Empty,
            PspMdrMinor: 0,
            FailureReason: "PayTR provider not yet implemented. Use Mock provider for dev."));
    }

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct = default)
    {
        // TODO(M7-impl): POST /odeme/iade with HMAC + amount + reason
        logger.LogWarning("PayTrPaymentProvider.RefundAsync — STUB (real impl pending).");
        return Task.FromResult(new RefundResult(
            Succeeded: false,
            ProviderRefundId: null,
            FailureReason: "PayTR refund not yet implemented."));
    }
}

public sealed class PayTrOptions
{
    public const string SectionName = "PayTr";
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantKey { get; set; } = string.Empty;
    public string MerchantSalt { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://www.paytr.com/odeme/api";
    public bool Sandbox { get; set; } = true;
}
