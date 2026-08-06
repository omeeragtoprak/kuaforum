using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record PlatformSettingsDto(
    Guid Id,
    decimal CommissionRateGlobal,
    string? CommissionPaymentIban,
    string? CommissionPaymentBankName,
    string? CommissionPaymentAccountHolderName,
    decimal DepositPercentMin,
    decimal DepositPercentMax);

public sealed record GetPlatformSettingsQuery() : IQuery<PlatformSettingsDto>;

/// <summary>Admin-only okuma. Cache YOK — RFC-012 "basit cache'siz okuma" kararı.</summary>
public sealed class GetPlatformSettingsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetPlatformSettingsQuery, Result<PlatformSettingsDto>>
{
    public async Task<Result<PlatformSettingsDto>> Handle(GetPlatformSettingsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var settings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure<PlatformSettingsDto>(Error.NotFound("PlatformSettings"));

        return Result.Success(new PlatformSettingsDto(
            settings.Id, settings.CommissionRateGlobal,
            settings.CommissionPaymentIban, settings.CommissionPaymentBankName, settings.CommissionPaymentAccountHolderName,
            settings.DepositPercentMin, settings.DepositPercentMax));
    }
}

public sealed record UpdatePlatformSettingsCommand(
    Guid AdminUserId,
    decimal CommissionRateGlobal,
    string? CommissionPaymentIban,
    string? CommissionPaymentBankName,
    string? CommissionPaymentAccountHolderName,
    decimal DepositPercentMin,
    decimal DepositPercentMax) : ICommand;

public sealed class UpdatePlatformSettingsCommandValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.CommissionRateGlobal).GreaterThan(0).LessThanOrEqualTo(0.5m);
        // Sıkı aralık denetimi (alt < üst, ikisi de %0-%100 içinde) domain'de
        // PlatformSettings.UpdateDepositPercentBounds() içinde yapılıyor.
        RuleFor(x => x.DepositPercentMin).GreaterThan(0);
        RuleFor(x => x.DepositPercentMax).GreaterThan(0);
    }
}

public sealed class UpdatePlatformSettingsCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<UpdatePlatformSettingsCommand, Result>
{
    public async Task<Result> Handle(UpdatePlatformSettingsCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var settings = await ctx.Set<PlatformSettings>().FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure(Error.NotFound("PlatformSettings"));

        var rateResult = settings.UpdateCommissionRateGlobal(request.CommissionRateGlobal);
        if (rateResult.IsFailure) return rateResult;

        var bankingResult = settings.UpdateBankingInfo(
            request.CommissionPaymentIban, request.CommissionPaymentBankName, request.CommissionPaymentAccountHolderName);
        if (bankingResult.IsFailure) return bankingResult;

        var depositBoundsResult = settings.UpdateDepositPercentBounds(request.DepositPercentMin, request.DepositPercentMax);
        if (depositBoundsResult.IsFailure) return depositBoundsResult;

        await audit.RecordAsync(
            "admin.platform_settings.updated",
            nameof(PlatformSettings),
            settings.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new
            {
                request.CommissionRateGlobal,
                hasIban = !string.IsNullOrWhiteSpace(request.CommissionPaymentIban),
                request.DepositPercentMin,
                request.DepositPercentMax,
            },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetBarberShopCommissionRateOverrideCommand(Guid AdminUserId, Guid BarberShopId, decimal? RateOverride) : ICommand;

public sealed class SetBarberShopCommissionRateOverrideCommandValidator : AbstractValidator<SetBarberShopCommissionRateOverrideCommand>
{
    public SetBarberShopCommissionRateOverrideCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.RateOverride).GreaterThan(0).LessThanOrEqualTo(0.5m).When(x => x.RateOverride.HasValue);
    }
}

public sealed class SetBarberShopCommissionRateOverrideCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<SetBarberShopCommissionRateOverrideCommand, Result>
{
    public async Task<Result> Handle(SetBarberShopCommissionRateOverrideCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<Domain.Modules.Barber.BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, ct);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        var result = shop.SetCommissionRateOverride(request.RateOverride);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.barbershop.commission_rate_override_set",
            nameof(Domain.Modules.Barber.BarberShop),
            shop.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { request.RateOverride },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
