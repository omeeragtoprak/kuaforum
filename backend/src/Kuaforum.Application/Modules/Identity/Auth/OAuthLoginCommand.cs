using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record OAuthLoginCommand(
    string Provider,
    string IdToken,
    string DeviceFingerprint,
    string? DeviceName,
    string? Ip) : ICommand<VerifyOtpResponse>;

public sealed class OAuthLoginCommandValidator : AbstractValidator<OAuthLoginCommand>
{
    public OAuthLoginCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().Must(p => p is "apple" or "google")
            .WithMessage("Provider must be 'apple' or 'google'.");
        RuleFor(x => x.IdToken).NotEmpty().MaximumLength(8192);
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(64);
    }
}

public sealed class OAuthLoginCommandHandler(
    IApplicationDbContext db,
    IEnumerable<IOAuthProvider> oauthProviders,
    IJwtTokenService jwtTokenService,
    IAuditLogger audit) : IRequestHandler<OAuthLoginCommand, Result<VerifyOtpResponse>>
{
    private const int MaxConcurrentDevicesPerUser = 5;
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<Result<VerifyOtpResponse>> Handle(OAuthLoginCommand request, CancellationToken cancellationToken)
    {
        var provider = oauthProviders.FirstOrDefault(p =>
            p.ProviderName.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return Result.Failure<VerifyOtpResponse>(Error.Validation($"OAuth provider '{request.Provider}' not configured."));

        var identity = await provider.VerifyAsync(request.IdToken, cancellationToken);
        if (identity is null)
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Invalid OAuth id token."));

        if (string.IsNullOrEmpty(identity.Email))
            return Result.Failure<VerifyOtpResponse>(Error.Validation("OAuth provider did not supply email."));

        var emailResult = EmailAddress.TryCreate(identity.Email);
        if (emailResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(emailResult.Error);
        var email = emailResult.Value;

        var ctx = (DbContext)db;
        var users = ctx.Set<User>();
        var user = await users.FirstOrDefaultAsync(u => u.EmailNormalized == email.Normalized, cancellationToken);
        if (user is null)
        {
            var createResult = User.CreateCustomerByOAuth(email, $"oauth-{request.Provider}", identity.DisplayName);
            if (createResult.IsFailure)
                return Result.Failure<VerifyOtpResponse>(createResult.Error);
            user = createResult.Value;
            users.Add(user);
        }
        else
        {
            if (!user.CanLogin)
                return Result.Failure<VerifyOtpResponse>(Error.Forbidden("Account cannot login."));
        }

        var loginResult = user.MarkLoggedIn(request.DeviceFingerprint, request.Ip);
        if (loginResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(loginResult.Error);

        var refreshTokens = ctx.Set<RefreshToken>();
        var existingActive = await refreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        if (existingActive.Count >= MaxConcurrentDevicesPerUser)
            existingActive.First().Revoke(RefreshTokenRevokedReason.DeviceLimitExceeded);

        var rawRefresh = jwtTokenService.GenerateRefreshTokenRaw();
        var refreshHash = jwtTokenService.HashRefreshToken(rawRefresh);
        var refreshToken = RefreshToken.Issue(user.Id, refreshHash, request.DeviceFingerprint, request.DeviceName, RefreshTokenLifetime);
        refreshTokens.Add(refreshToken);

        var accessToken = jwtTokenService.Issue(user);

        await audit.RecordAsync(
            "user.login.oauth",
            nameof(User),
            user.Id.ToString(),
            actorUserId: user.Id,
            payload: new { provider = request.Provider, providerUserId = identity.ProviderUserId },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyOtpResponse(
            accessToken.AccessToken,
            rawRefresh,
            accessToken.AccessExpiresAt,
            refreshToken.ExpiresAt,
            new AuthenticatedUserDto(user.Id, user.PhoneE164, user.DisplayName, user.Role)));
    }
}
