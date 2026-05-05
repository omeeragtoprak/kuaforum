using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record VerifyOtpCommand(
    Guid OtpId,
    string Code,
    string DeviceFingerprint,
    string? DeviceName,
    string? Ip) : ICommand<VerifyOtpResponse>;

public sealed record AuthenticatedUserDto(
    Guid Id,
    string? Phone,
    string? DisplayName,
    UserRole Role);

public sealed record VerifyOtpResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt,
    AuthenticatedUserDto User);

public sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.OtpId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$");
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceName).MaximumLength(100);
    }
}

public sealed class VerifyOtpCommandHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>
{
    private const int MaxConcurrentDevicesPerUser = 5;
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<Result<VerifyOtpResponse>> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var otp = await ctx.Set<OtpCode>().FirstOrDefaultAsync(o => o.Id == request.OtpId, cancellationToken);
        if (otp is null)
            return Result.Failure<VerifyOtpResponse>(Error.NotFound("OTP"));

        var hash = otpService.HashCode(otp.PhoneE164, request.Code);
        var verifyResult = otp.AttemptVerify(hash);
        await db.SaveChangesAsync(cancellationToken);

        if (verifyResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(verifyResult.Error);

        if (!verifyResult.Value)
            return Result.Failure<VerifyOtpResponse>(Error.Validation("Doğrulama kodu hatalı."));

        var phoneResult = PhoneE164.TryCreate(otp.PhoneE164);
        if (phoneResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(phoneResult.Error);

        var users = ctx.Set<User>();
        var user = await users.FirstOrDefaultAsync(u => u.PhoneE164 == otp.PhoneE164, cancellationToken);
        if (user is null)
        {
            var createResult = User.CreateCustomerByPhone(phoneResult.Value);
            if (createResult.IsFailure)
                return Result.Failure<VerifyOtpResponse>(createResult.Error);
            user = createResult.Value;
            users.Add(user);
        }
        else
        {
            if (!user.CanLogin)
                return Result.Failure<VerifyOtpResponse>(Error.Forbidden("Bu hesap ile giriş yapılamaz."));
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
        {
            var oldest = existingActive.First();
            oldest.Revoke(RefreshTokenRevokedReason.DeviceLimitExceeded);
        }

        var rawRefresh = jwtTokenService.GenerateRefreshTokenRaw();
        var refreshHash = jwtTokenService.HashRefreshToken(rawRefresh);
        var refreshToken = RefreshToken.Issue(
            user.Id,
            refreshHash,
            request.DeviceFingerprint,
            request.DeviceName,
            RefreshTokenLifetime);
        refreshTokens.Add(refreshToken);

        var accessToken = jwtTokenService.Issue(user);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyOtpResponse(
            accessToken.AccessToken,
            rawRefresh,
            accessToken.AccessExpiresAt,
            refreshToken.ExpiresAt,
            new AuthenticatedUserDto(user.Id, user.PhoneE164, user.DisplayName, user.Role)));
    }
}
