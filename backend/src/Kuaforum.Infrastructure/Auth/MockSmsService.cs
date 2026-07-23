using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// Development / test SMS provider that logs OTP codes instead of sending real SMS.
/// Replace with NetGsmSmsService in staging/production (Sms:Provider=netgsm).
///
/// Safety net: if this ever ends up wired in Production (e.g. Sms:Provider
/// misconfigured/missing), it refuses to log the OTP code — logging a live OTP
/// code in Production is a credential-leak / KVKK risk, not an acceptable
/// "dev convenience" trade-off.
/// </summary>
public sealed class MockSmsService(IHostEnvironment env, ILogger<MockSmsService> logger) : ISmsService
{
    public Task<bool> SendOtpAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        var maskedPhone = phoneE164.Length >= 13
            ? $"{phoneE164[..3]} {phoneE164.Substring(3, 3)} •• •• {phoneE164[^2..]}"
            : phoneE164;

        if (env.IsProduction())
        {
            logger.LogError(
                "MockSmsService invoked in Production for {Phone} — refusing to log OTP code. " +
                "This means Sms:Provider is misconfigured (expected 'netgsm'); fix NetGsm__* env vars.",
                maskedPhone);
            return Task.FromResult(false);
        }

        logger.LogWarning(
            "MOCK SMS to {Phone}: OTP code is {Code}. (Replace with NetGSM in staging.)",
            maskedPhone,
            code);
        return Task.FromResult(true);
    }
}
