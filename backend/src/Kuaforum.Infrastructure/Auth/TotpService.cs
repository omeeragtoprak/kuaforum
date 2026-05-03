using Kuaforum.Application.Common.Interfaces;
using OtpNet;

namespace Kuaforum.Infrastructure.Auth;

public sealed class TotpService : ITotpService
{
    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20); // 160-bit (RFC 6238 recommended)
        return Base32Encoding.ToString(bytes);
    }

    public bool Verify(string base32Secret, string code, int windowSize = 1)
    {
        if (string.IsNullOrEmpty(base32Secret) || string.IsNullOrEmpty(code))
            return false;

        try
        {
            var bytes = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(bytes);
            var window = new VerificationWindow(previous: windowSize, future: windowSize);
            return totp.VerifyTotp(code, out _, window);
        }
        catch
        {
            return false;
        }
    }

    public string FormatProvisioningUri(string base32Secret, string accountName, string issuer)
    {
        var encodedAccount = Uri.EscapeDataString(accountName);
        var encodedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={base32Secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }
}
