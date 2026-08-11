using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record AdminLoginCommand(
    string Email,
    string Password,
    string TotpCode,
    string DeviceFingerprint,
    string? DeviceName,
    string? Ip) : ICommand<VerifyOtpResponse>;

public sealed class AdminLoginCommandValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.TotpCode).NotEmpty().Matches(@"^\d{6}$");
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(64);
    }
}

public sealed class AdminLoginCommandHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITotpService totpService,
    ISymmetricEncryption encryption,
    IJwtTokenService jwtTokenService,
    IAuditLogger audit) : IRequestHandler<AdminLoginCommand, Result<VerifyOtpResponse>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<Result<VerifyOtpResponse>> Handle(AdminLoginCommand request, CancellationToken cancellationToken)
    {
        var emailResult = EmailAddress.TryCreate(request.Email);
        if (emailResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(emailResult.Error);

        var ctx = (DbContext)db;
        var user = await ctx.Set<User>()
            .FirstOrDefaultAsync(u => u.EmailNormalized == emailResult.Value.Normalized
                                       && u.Role == UserRole.Admin, cancellationToken);
        if (user is null) return GenericFailure();

        var cred = await ctx.Set<AdminCredential>()
            .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);
        if (cred is null) return GenericFailure();

        if (cred.IsLocked())
            return Result.Failure<VerifyOtpResponse>(Error.Forbidden("Hesap geçici olarak kilitli. Daha sonra tekrar deneyin."));

        if (!user.CanLogin)
            return Result.Failure<VerifyOtpResponse>(Error.Forbidden("Hesap aktif değil."));

        if (!passwordHasher.Verify(cred.PasswordHash, request.Password))
        {
            cred.RecordFailedAttempt();
            await audit.RecordAsync("admin.login.failed", nameof(User), user.Id.ToString(),
                actorUserId: user.Id, payload: new { reason = "bad_password" }, ct: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return GenericFailure();
        }

        var totpSecret = encryption.Decrypt(cred.TotpSecretEncrypted);
        if (!totpService.Verify(totpSecret, request.TotpCode))
        {
            cred.RecordFailedAttempt();
            await audit.RecordAsync("admin.login.failed", nameof(User), user.Id.ToString(),
                actorUserId: user.Id, payload: new { reason = "bad_totp" }, ct: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return GenericFailure();
        }

        cred.RecordSuccessfulLogin();
        var loginResult = user.MarkLoggedIn(request.DeviceFingerprint, request.Ip);
        if (loginResult.IsFailure)
            return Result.Failure<VerifyOtpResponse>(loginResult.Error);

        var rawRefresh = jwtTokenService.GenerateRefreshTokenRaw();
        var refreshHash = jwtTokenService.HashRefreshToken(rawRefresh);
        var refreshToken = RefreshToken.Issue(user.Id, refreshHash, request.DeviceFingerprint, request.DeviceName, RefreshTokenLifetime);
        ctx.Set<RefreshToken>().Add(refreshToken);

        var access = jwtTokenService.Issue(user);

        await audit.RecordAsync("admin.login.success", nameof(User), user.Id.ToString(),
            actorUserId: user.Id, payload: new { request.DeviceName }, ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyOtpResponse(
            access.AccessToken,
            rawRefresh,
            access.AccessExpiresAt,
            refreshToken.ExpiresAt,
            new AuthenticatedUserDto(user.Id, user.PhoneE164, user.DisplayName, user.Role),
            false));
    }

    private static Result<VerifyOtpResponse> GenericFailure() =>
        Result.Failure<VerifyOtpResponse>(Error.Unauthorized("Kullanıcı adı veya şifre hatalı."));
}
