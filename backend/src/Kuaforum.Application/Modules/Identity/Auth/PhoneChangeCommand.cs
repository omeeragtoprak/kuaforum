using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record InitiatePhoneChangeCommand(Guid UserId, string NewPhone) : ICommand<SendOtpResponse>;

public sealed class InitiatePhoneChangeCommandValidator : AbstractValidator<InitiatePhoneChangeCommand>
{
    public InitiatePhoneChangeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPhone).NotEmpty().MaximumLength(16);
    }
}

public sealed class InitiatePhoneChangeCommandHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ISmsService smsService,
    IRateLimiter rateLimiter,
    IAuditLogger audit) : IRequestHandler<InitiatePhoneChangeCommand, Result<SendOtpResponse>>
{
    public async Task<Result<SendOtpResponse>> Handle(InitiatePhoneChangeCommand request, CancellationToken cancellationToken)
    {
        var phoneResult = PhoneE164.TryCreate(request.NewPhone);
        if (phoneResult.IsFailure)
            return Result.Failure<SendOtpResponse>(phoneResult.Error);

        var ctx = (DbContext)db;
        var users = ctx.Set<User>();
        var existing = await users.AnyAsync(u => u.PhoneE164 == phoneResult.Value.Value && u.Id != request.UserId, cancellationToken);
        if (existing)
            return Result.Failure<SendOtpResponse>(Error.Conflict("Phone number is already used by another account."));

        var rate = await rateLimiter.CheckAsync(
            bucket: $"phonechange:{request.UserId}",
            max: 3,
            window: TimeSpan.FromHours(1),
            ct: cancellationToken);
        if (!rate.Allowed)
            return Result.Failure<SendOtpResponse>(Error.Validation($"Too many attempts. Retry in {rate.RetryAfter?.TotalMinutes:0} min."));

        var phone = phoneResult.Value;
        var code = otpService.GenerateNumericCode();
        var hash = otpService.HashCode(phone.Value, code);
        var otp = OtpCode.Issue(phone.Value, hash, OtpPurpose.PhoneChange);
        ctx.Set<OtpCode>().Add(otp);
        await db.SaveChangesAsync(cancellationToken);

        var sent = await smsService.SendOtpAsync(phone.Value, code, cancellationToken);
        if (!sent)
            return Result.Failure<SendOtpResponse>(Error.Validation("SMS gateway temporarily unavailable."));

        await audit.RecordAsync(
            "user.phone.change.initiated",
            nameof(User),
            request.UserId.ToString(),
            actorUserId: request.UserId,
            payload: new { newPhoneMasked = phone.Masked() },
            ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new SendOtpResponse(otp.Id, otp.ExpiresAt));
    }
}

public sealed record ConfirmPhoneChangeCommand(Guid UserId, Guid OtpId, string Code) : ICommand;

public sealed class ConfirmPhoneChangeCommandValidator : AbstractValidator<ConfirmPhoneChangeCommand>
{
    public ConfirmPhoneChangeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OtpId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$");
    }
}

public sealed class ConfirmPhoneChangeCommandHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    IAuditLogger audit) : IRequestHandler<ConfirmPhoneChangeCommand, Result>
{
    public async Task<Result> Handle(ConfirmPhoneChangeCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var otp = await ctx.Set<OtpCode>().FirstOrDefaultAsync(o =>
            o.Id == request.OtpId && o.Purpose == OtpPurpose.PhoneChange, cancellationToken);
        if (otp is null) return Result.Failure(Error.NotFound("OTP"));

        var hash = otpService.HashCode(otp.PhoneE164, request.Code);
        var verifyResult = otp.AttemptVerify(hash);
        await db.SaveChangesAsync(cancellationToken);

        if (verifyResult.IsFailure) return verifyResult;
        if (!verifyResult.Value) return Result.Failure(Error.Validation("Invalid OTP code."));

        var phoneResult = PhoneE164.TryCreate(otp.PhoneE164);
        if (phoneResult.IsFailure) return phoneResult;

        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var oldPhone = user.PhoneE164;
        var changeResult = user.ChangePhone(phoneResult.Value);
        if (changeResult.IsFailure) return changeResult;

        await audit.RecordAsync(
            "user.phone.change.confirmed",
            nameof(User),
            user.Id.ToString(),
            actorUserId: user.Id,
            payload: new { oldPhoneSuffix = oldPhone?.Length >= 4 ? oldPhone[^4..] : null },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
