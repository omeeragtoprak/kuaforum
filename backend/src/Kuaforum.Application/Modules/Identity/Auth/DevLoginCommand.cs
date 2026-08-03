using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

/// <summary>
/// DEVELOPMENT-ONLY login. Bypasses OTP/TOTP and issues tokens directly for a known demo identity.
/// Endpoint MUST be guarded so only Development environment can map it.
/// </summary>
public sealed record DevLoginCommand(
    DevLoginRole Role,
    string DeviceFingerprint,
    string? DeviceName,
    string? Ip) : ICommand<VerifyOtpResponse>;

public enum DevLoginRole
{
    Customer = 0,
    BarberOwner = 1,
    Admin = 2,
    BarberOwnerDraft = 3,

    /// <summary>
    /// RFC-008 — rıza akışının elle test kapısı: her dev-login'de güncel ToS rızası
    /// OLMAYAN taze bir customer döner (varsa granted ToS rızaları revoke edilir),
    /// böylece consent ekranı her girişte yeniden açılır.
    /// </summary>
    CustomerFresh = 4,
}

public sealed class DevLoginCommandValidator : AbstractValidator<DevLoginCommand>
{
    public DevLoginCommandValidator()
    {
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceName).MaximumLength(100);
    }
}

public sealed class DevLoginCommandHandler(
    IApplicationDbContext db,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<DevLoginCommand, Result<VerifyOtpResponse>>
{
    private const int MaxConcurrentDevicesPerUser = 5;
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private const string CustomerDemoPhone = "+905551234567";
    private const string CustomerFreshDemoPhone = "+905551234599";
    private const string BarberOwnerDemoPhone = "+905001000001";
    private const string BarberOwnerDraftDemoPhone = "+905001000099";

    public async Task<Result<VerifyOtpResponse>> Handle(DevLoginCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var users = ctx.Set<User>();
        User? user = null;

        switch (request.Role)
        {
            case DevLoginRole.Customer:
            {
                user = await users.FirstOrDefaultAsync(u => u.PhoneE164 == CustomerDemoPhone, cancellationToken);
                if (user is null)
                {
                    var phoneResult = PhoneE164.TryCreate(CustomerDemoPhone);
                    if (phoneResult.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(phoneResult.Error);
                    var createResult = User.CreateCustomerByPhone(phoneResult.Value, "Demo Müşteri");
                    if (createResult.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(createResult.Error);
                    user = createResult.Value;
                    users.Add(user);
                }
                break;
            }

            case DevLoginRole.CustomerFresh:
            {
                // RFC-008 — consent'siz taze customer: kullanıcı yoksa yaratılır; varsa
                // granted ToS rızaları revoke edilir. Sonuç: requiresTosConsent her
                // dev-login'de true → rıza ekranı elle test edilebilir.
                user = await users.FirstOrDefaultAsync(u => u.PhoneE164 == CustomerFreshDemoPhone, cancellationToken);
                if (user is null)
                {
                    var phoneResult = PhoneE164.TryCreate(CustomerFreshDemoPhone);
                    if (phoneResult.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(phoneResult.Error);
                    var createResult = User.CreateCustomerByPhone(phoneResult.Value, "Demo Müşteri (Rıza)");
                    if (createResult.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(createResult.Error);
                    user = createResult.Value;
                    users.Add(user);
                }
                else
                {
                    var grantedTosConsents = await ctx.Set<Consent>()
                        .Where(c => c.UserId == user.Id
                            && c.Purpose == ConsentPurpose.TermsOfService
                            && c.Granted
                            && c.RevokedAt == null)
                        .ToListAsync(cancellationToken);
                    foreach (var consent in grantedTosConsents)
                        consent.Revoke();
                }
                break;
            }

            case DevLoginRole.BarberOwner:
            {
                user = await users.FirstOrDefaultAsync(u => u.PhoneE164 == BarberOwnerDemoPhone, cancellationToken);
                if (user is null)
                    return Result.Failure<VerifyOtpResponse>(Error.NotFound(
                        "Demo kuaför kullanıcısı bulunamadı. DemoBarberSeeder çalıştırılmalı."));
                if (user.Role != UserRole.BarberOwner)
                {
                    var promote = user.PromoteToBarberOwner();
                    if (promote.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(promote.Error);
                }
                break;
            }

            case DevLoginRole.BarberOwnerDraft:
            {
                user = await users.FirstOrDefaultAsync(u => u.PhoneE164 == BarberOwnerDraftDemoPhone, cancellationToken);
                if (user is null)
                    return Result.Failure<VerifyOtpResponse>(Error.NotFound(
                        "Demo Draft kuaför kullanıcısı bulunamadı. DemoBarberSeeder çalıştırılmalı."));
                if (user.Role != UserRole.BarberOwner)
                {
                    var promote = user.PromoteToBarberOwner();
                    if (promote.IsFailure)
                        return Result.Failure<VerifyOtpResponse>(promote.Error);
                }
                break;
            }

            case DevLoginRole.Admin:
            {
                user = await users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin, cancellationToken);
                if (user is null)
                    return Result.Failure<VerifyOtpResponse>(Error.NotFound(
                        "Admin kullanıcısı bulunamadı. AdminSeeder çalıştırılmalı."));
                break;
            }

            default:
                return Result.Failure<VerifyOtpResponse>(Error.Validation("Bilinmeyen rol."));
        }

        if (!user.CanLogin)
            return Result.Failure<VerifyOtpResponse>(Error.Forbidden("Bu hesap ile giriş yapılamaz."));

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
            existingActive.First().Revoke(RefreshTokenRevokedReason.DeviceLimitExceeded);
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
