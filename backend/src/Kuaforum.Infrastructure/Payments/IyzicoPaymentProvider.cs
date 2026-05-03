using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Payments;

/// <summary>
/// Iyzico Pazaryeri provider — fallback PSP. T+3 weeks holdback dezavantajı.
/// Real impl: install Iyzipay nuget + use IyzipayClient with marketplace product.
/// </summary>
public sealed class IyzicoPaymentProvider(
    IOptions<IyzicoOptions> options,
    ILogger<IyzicoPaymentProvider> logger) : IPaymentProvider
{
    public PaymentProviderName ProviderName => PaymentProviderName.Iyzico;

    private readonly IyzicoOptions _options = options.Value;

    public Task<CheckoutSession> InitiateAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Iyzico not configured. Set Iyzico:ApiKey/SecretKey or use Mock.");

        logger.LogWarning("IyzicoPaymentProvider.InitiateAsync — STUB (real impl pending).");
        return Task.FromResult(new CheckoutSession(
            CheckoutUrl: "https://sandbox-api.iyzipay.com/payment/iyzipos/checkoutform/auth/PLACEHOLDER",
            ProviderToken: $"iyzico-placeholder-{request.PaymentId:N}",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30)));
    }

    public Task<PaymentVerification> VerifyAsync(string providerToken, CancellationToken ct = default)
    {
        return Task.FromResult(new PaymentVerification(
            Succeeded: false,
            ProviderTransactionId: string.Empty,
            PspMdrMinor: 0,
            FailureReason: "Iyzico provider not yet implemented."));
    }

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new RefundResult(
            Succeeded: false,
            ProviderRefundId: null,
            FailureReason: "Iyzico refund not yet implemented."));
    }
}

public sealed class IyzicoOptions
{
    public const string SectionName = "Iyzico";
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://sandbox-api.iyzipay.com";
    public bool Sandbox { get; set; } = true;
}
