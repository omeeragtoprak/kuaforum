using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string DeviceFingerprint,
    string? Ip) : ICommand<VerifyOtpResponse>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(64);
    }
}

public sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<RefreshTokenCommand, Result<VerifyOtpResponse>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<Result<VerifyOtpResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var hash = jwtTokenService.HashRefreshToken(request.RefreshToken);

        var refreshTokens = ctx.Set<RefreshToken>();
        var token = await refreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Yenileme tokeni geçersiz."));

        if (token.IsRevoked)
        {
            // Reuse detected: revoke all tokens of this user
            token.MarkReuseDetected(request.Ip);
            var siblings = await refreshTokens
                .Where(t => t.UserId == token.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var s in siblings)
                s.Revoke(RefreshTokenRevokedReason.TokenReuse);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Yenileme tokeni yeniden kullanım tespit edildi. Lütfen tekrar giriş yapın."));
        }

        if (token.IsExpired)
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Yenileme tokeninin süresi doldu."));

        if (token.DeviceFingerprint != request.DeviceFingerprint)
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Cihaz parmak izi eşleşmiyor."));

        var users = ctx.Set<User>();
        var user = await users.FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        if (user is null || !user.CanLogin)
            return Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Hesap aktif değil."));

        var newRawRefresh = jwtTokenService.GenerateRefreshTokenRaw();
        var newRefreshHash = jwtTokenService.HashRefreshToken(newRawRefresh);
        var newRefresh = RefreshToken.Issue(
            user.Id,
            newRefreshHash,
            request.DeviceFingerprint,
            token.DeviceName,
            RefreshTokenLifetime);
        refreshTokens.Add(newRefresh);

        token.Rotate(newRefresh.Id, request.Ip);

        var newAccess = jwtTokenService.Issue(user);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyOtpResponse(
            newAccess.AccessToken,
            newRawRefresh,
            newAccess.AccessExpiresAt,
            newRefresh.ExpiresAt,
            new AuthenticatedUserDto(user.Id, user.PhoneE164, user.DisplayName, user.Role),
            false));
    }
}
