using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// Development / test SMS provider that logs OTP codes instead of sending real SMS.
/// Replace with NetGSMSmsService in staging/production.
/// </summary>
public sealed class MockSmsService(ILogger<MockSmsService> logger) : ISmsService
{
    public Task<bool> SendOtpAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        var maskedPhone = phoneE164.Length >= 13
            ? $"{phoneE164[..3]} {phoneE164.Substring(3, 3)} •• •• {phoneE164[^2..]}"
            : phoneE164;
        logger.LogWarning(
            "MOCK SMS to {Phone}: OTP code is {Code}. (Replace with NetGSM in staging.)",
            maskedPhone,
            code);
        return Task.FromResult(true);
    }
}
