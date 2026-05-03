using Kuaforum.Domain.Modules.Identity;

namespace Kuaforum.Application.Common.Interfaces;

public sealed record IssuedTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt);

public interface IJwtTokenService
{
    IssuedTokens Issue(User user);
    string GenerateRefreshTokenRaw();
    string HashRefreshToken(string rawToken);
}
