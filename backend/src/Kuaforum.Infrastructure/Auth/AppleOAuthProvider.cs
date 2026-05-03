using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// Verifies Apple ID tokens against Apple's JWKS endpoint.
/// Production: configure AppleOAuthOptions.ClientId (bundle id "com.kuaforum.app" or service id).
/// </summary>
public sealed class AppleOAuthProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<AppleOAuthProvider> logger,
    Microsoft.Extensions.Options.IOptions<AppleOAuthOptions> options) : IOAuthProvider
{
    public string ProviderName => "apple";

    private readonly AppleOAuthOptions _options = options.Value;
    private static readonly JwtSecurityTokenHandler Handler = new();

    public async Task<OAuthIdentity?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ClientId))
        {
            logger.LogWarning("Apple OAuth client id missing; rejecting all tokens.");
            return null;
        }

        try
        {
            var http = httpClientFactory.CreateClient();
            var jwks = await http.GetFromJsonAsync<AppleJwks>("https://appleid.apple.com/auth/keys", ct);
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
                ValidIssuer = "https://appleid.apple.com",
                ValidAudiences = _options.AllowedAudiences.Length > 0
                    ? _options.AllowedAudiences
                    : [_options.ClientId],
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            var principal = Handler.ValidateToken(idToken, validationParameters, out _);
            return new OAuthIdentity(
                ProviderUserId: principal.FindFirst("sub")?.Value ?? string.Empty,
                Email: principal.FindFirst("email")?.Value,
                DisplayName: principal.FindFirst("name")?.Value, // Apple genelde sadece ilk login'de döner
                EmailVerified: bool.TryParse(principal.FindFirst("email_verified")?.Value, out var v) && v);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Apple ID token verification failed.");
            return null;
        }
    }

    private sealed record AppleJwks(AppleJwk[] Keys);
    private sealed record AppleJwk(string Kty, string Kid, string Use, string Alg, string N, string E);
}

public sealed class AppleOAuthOptions
{
    public const string SectionName = "OAuth:Apple";
    public string ClientId { get; set; } = string.Empty;
    public string[] AllowedAudiences { get; set; } = [];
}
