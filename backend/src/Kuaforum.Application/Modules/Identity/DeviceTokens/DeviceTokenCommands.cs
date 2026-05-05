using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.DeviceTokens;

public sealed record RegisterDeviceTokenCommand(
    Guid UserId,
    string FcmToken,
    DevicePlatform Platform,
    string? AppVersion,
    string? DeviceModel) : ICommand;

public sealed class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FcmToken).NotEmpty().MaximumLength(512);
        RuleFor(x => x.AppVersion).MaximumLength(32);
        RuleFor(x => x.DeviceModel).MaximumLength(64);
    }
}

public sealed class RegisterDeviceTokenCommandHandler(IApplicationDbContext db)
    : IRequestHandler<RegisterDeviceTokenCommand, Result>
{
    public async Task<Result> Handle(RegisterDeviceTokenCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var existing = await ctx.Set<DeviceToken>()
            .FirstOrDefaultAsync(d => d.FcmToken == request.FcmToken, ct);

        if (existing is null)
        {
            var entry = DeviceToken.Register(
                request.UserId, request.FcmToken, request.Platform,
                request.AppVersion, request.DeviceModel);
            ctx.Set<DeviceToken>().Add(entry);
        }
        else if (existing.UserId != request.UserId)
        {
            existing.ReassignTo(request.UserId, request.AppVersion, request.DeviceModel);
        }
        else
        {
            existing.Refresh(request.AppVersion, request.DeviceModel);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record UnregisterDeviceTokenCommand(Guid UserId, string FcmToken) : ICommand;

public sealed class UnregisterDeviceTokenCommandHandler(IApplicationDbContext db)
    : IRequestHandler<UnregisterDeviceTokenCommand, Result>
{
    public async Task<Result> Handle(UnregisterDeviceTokenCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var existing = await ctx.Set<DeviceToken>()
            .FirstOrDefaultAsync(d => d.FcmToken == request.FcmToken && d.UserId == request.UserId, ct);
        if (existing is null) return Result.Success();
        existing.Deactivate("user.logout");
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
