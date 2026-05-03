namespace Kuaforum.Application.Common.Interfaces;

public interface IOtpService
{
    string GenerateNumericCode(int digits = 6);
    string HashCode(string phoneE164, string code);
}
