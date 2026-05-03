using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record SendOtpCommand(string Phone, OtpPurpose Purpose) : ICommand<SendOtpResponse>;

public sealed record SendOtpResponse(Guid OtpId, DateTimeOffset ExpiresAt);

public sealed class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(16);
        RuleFor(x => x.Purpose).IsInEnum();
    }
}

public sealed class SendOtpCommandHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ISmsService smsService,
    IRateLimiter rateLimiter)
    : IRequestHandler<SendOtpCommand, Result<SendOtpResponse>>
{
    public async Task<Result<SendOtpResponse>> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        var phoneResult = PhoneE164.TryCreate(request.Phone);
        if (phoneResult.IsFailure)
            return Result.Failure<SendOtpResponse>(phoneResult.Error);

        var phone = phoneResult.Value;
        var rate = await rateLimiter.CheckAsync(
            bucket: $"otp:{phone.Value}",
            max: 3,
            window: TimeSpan.FromHours(1),
            ct: cancellationToken);
        if (!rate.Allowed)
            return Result.Failure<SendOtpResponse>(
                Error.Validation($"Too many OTP requests. Retry in {rate.RetryAfter?.TotalMinutes:0} min."));

        var ctx = ((DbContext)db);
        var otpSet = ctx.Set<OtpCode>();
        var pending = await otpSet
            .Where(o => o.PhoneE164 == phone.Value && o.Status == OtpCodeStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var p in pending)
        {
            otpSet.Remove(p);
        }

        var code = otpService.GenerateNumericCode();
        var hash = otpService.HashCode(phone.Value, code);
        var otp = OtpCode.Issue(phone.Value, hash, request.Purpose);
        otpSet.Add(otp);
        await db.SaveChangesAsync(cancellationToken);

        var sent = await smsService.SendOtpAsync(phone.Value, code, cancellationToken);
        if (!sent)
            return Result.Failure<SendOtpResponse>(Error.Validation("SMS gateway temporarily unavailable."));

        return Result.Success(new SendOtpResponse(otp.Id, otp.ExpiresAt));
    }
}
