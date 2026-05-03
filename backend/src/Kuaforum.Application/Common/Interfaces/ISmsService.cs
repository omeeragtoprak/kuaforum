namespace Kuaforum.Application.Common.Interfaces;

public interface ISmsService
{
    Task<bool> SendOtpAsync(string phoneE164, string code, CancellationToken ct = default);
}
