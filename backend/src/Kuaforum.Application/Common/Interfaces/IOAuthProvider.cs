namespace Kuaforum.Application.Common.Interfaces;

public sealed record OAuthIdentity(
    string ProviderUserId,
    string? Email,
    string? DisplayName,
    bool EmailVerified);

public interface IOAuthProvider
{
    string ProviderName { get; }
    Task<OAuthIdentity?> VerifyAsync(string idToken, CancellationToken ct = default);
}
