using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// Verifies Google ID tokens against Google's published JWKS.
/// Production: configure GoogleOAuthOptions.ClientId with your Google OAuth client ID.
/// </summary>
public sealed class GoogleOAuthProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleOAuthProvider> logger,
    Microsoft.Extensions.Options.IOptions<GoogleOAuthOptions> options) : IOAuthProvider
{
    public string ProviderName => "google";

    private readonly GoogleOAuthOptions _options = options.Value;
    private static readonly JwtSecurityTokenHandler Handler = new();

    public async Task<OAuthIdentity?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ClientId))
        {
            logger.LogWarning("Google OAuth client id missing; rejecting all tokens.");
            return null;
        }

        try
        {
            var http = httpClientFactory.CreateClient();
            var jwks = await http.GetFromJsonAsync<GoogleJwks>("https://www.googleapis.com/oauth2/v3/certs", ct);
            if (jwks is null) return null;

            var signingKeys = jwks.Keys.Select(k => new JsonWebKey
            {
                Kty = k.Kty,
                Kid = k.Kid,
                Use = k.Use,
                Alg = k.Alg,
                N = k.N,
                E = k.E,
            }).Cast<SecurityKey>().ToList();

            var validationParameters = new TokenValidationParameters
            {
                ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
                ValidAudience = _options.ClientId,
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            var principal = Handler.ValidateToken(idToken, validationParameters, out _);
            return new OAuthIdentity(
                ProviderUserId: principal.FindFirst("sub")?.Value ?? string.Empty,
                Email: principal.FindFirst("email")?.Value,
                DisplayName: principal.FindFirst("name")?.Value,
                EmailVerified: bool.TryParse(principal.FindFirst("email_verified")?.Value, out var v) && v);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google ID token verification failed.");
            return null;
        }
    }

    private sealed record GoogleJwks(GoogleJwk[] Keys);
    private sealed record GoogleJwk(string Kty, string Kid, string Use, string Alg, string N, string E);
}

public sealed class GoogleOAuthOptions
{
    public const string SectionName = "OAuth:Google";
    public string ClientId { get; set; } = string.Empty;
}
