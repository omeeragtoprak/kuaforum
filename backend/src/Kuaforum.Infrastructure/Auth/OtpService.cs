using System.Security.Cryptography;
using System.Text;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Auth;

public sealed class OtpService(IOptions<OtpOptions> options) : IOtpService
{
    private readonly OtpOptions _options = options.Value;

    public string GenerateNumericCode(int digits = 6)
    {
        if (digits is < 4 or > 8)
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be 4-8.");

        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var num = BitConverter.ToUInt32(buffer);
        var max = (uint)Math.Pow(10, digits);
        var code = num % max;
        return code.ToString().PadLeft(digits, '0');
    }

    public string HashCode(string phoneE164, string code)
    {
        var pepper = _options.Pepper ?? string.Empty;
        var input = $"{phoneE164}|{code}|{pepper}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

public sealed class OtpOptions
{
    public const string SectionName = "Otp";
    public string? Pepper { get; set; }
}

internal static class OtpServiceExtensions
{
}
