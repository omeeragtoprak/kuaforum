using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// NetGSM HTTP API SMS provider. Pay-as-you-go (~₺0.05/SMS).
/// Falls back to MockSmsService-style logging behavior if NetGSM:UserCode is
/// missing — but ONLY outside Production. In Production, a missing config is
/// treated as a hard failure (Error log, no code in the log, return false)
/// instead of silently leaking the OTP code to logs (KVKK + credential-leak risk).
///
/// IMPORTANT: This is a SCAFFOLDED stub. Real impl pending NetGSM merchant
/// account + IP whitelist + originator approval. See
/// https://www.netgsm.com.tr/dokuman/ for API spec.
/// </summary>
public sealed class NetGsmSmsService(
    IHttpClientFactory httpClientFactory,
    IOptions<NetGsmOptions> options,
    IHostEnvironment env,
    ILogger<NetGsmSmsService> logger) : ISmsService
{
    private readonly NetGsmOptions _options = options.Value;

    public async Task<bool> SendOtpAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.UserCode))
        {
            if (env.IsProduction())
            {
                // Do NOT log the phone number or the OTP code here — this branch means
                // NetGsm__UserCode/Password/Originator env vars are missing in prod,
                // which is a config bug, not a case to silently degrade into logging secrets.
                logger.LogError(
                    "NetGsmSmsService: NetGsm:UserCode is not configured in Production. " +
                    "OTP SMS send refused (no fallback log-the-code behavior in Production). " +
                    "Set NetGsm__UserCode / NetGsm__Password / NetGsm__Originator env vars.");
                return false;
            }

            // Dev/staging convenience: behave like MockSmsService so local/dev flows
            // still work without a real NetGSM account.
            var maskedPhone = phoneE164.Length >= 13
                ? $"{phoneE164[..3]} {phoneE164.Substring(3, 3)} •• •• {phoneE164[^2..]}"
                : phoneE164;
            logger.LogWarning(
                "NetGSM not configured. Fallback to MOCK SMS to {Phone}: code is {Code}.",
                maskedPhone, code);
            return true;
        }

        // TODO(M11-impl): real NetGSM HTTP call
        //   GET https://api.netgsm.com.tr/sms/send/otp?
        //     usercode=...&password=...&gsmno=...&message=...&msgheader=...
        // Reference: https://www.netgsm.com.tr/dokuman/sms-gonderim/
        logger.LogWarning("NetGsmSmsService.SendOtpAsync — STUB (real impl pending). " +
            "Account opening + originator approval needed.");
        return await Task.FromResult(true);
    }
}

public sealed class NetGsmOptions
{
    public const string SectionName = "NetGsm";
    public string UserCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Originator { get; set; } = "KUAFORUM";
    public string ApiBaseUrl { get; set; } = "https://api.netgsm.com.tr";
}
