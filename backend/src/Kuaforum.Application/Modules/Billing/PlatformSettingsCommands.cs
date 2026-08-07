using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Application.Modules.Billing;

public sealed record PlatformSettingsDto(
    Guid Id,
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
            settings.Id,
            settings.CommissionPaymentIban, settings.CommissionPaymentBankName, settings.CommissionPaymentAccountHolderName,
            settings.DepositPercentMin, settings.DepositPercentMax));
    }
}

/// <summary>
/// RFC-013 review I1: admin DepositPercentMin/Max'i daraltınca, o anki aralığın dışında
/// kalan (ör. DepositPercent=0.60 iken üst sınır 0.50'ye çekilen) shop'lar OTOMATİK
/// CLAMP EDİLMEZ — owner'ın ayarını admin'in sessizce değiştirmesi yanlış olurdu; bir
/// sonraki PUT /booking-policy'de owner zaten yeni aralığa uymak zorunda kalacak
/// (BookingPolicy.Create domain guard'ı). Bu response alanı yalnızca admin'e görünürlük
/// sağlar.
/// </summary>
public sealed record UpdatePlatformSettingsResponse(int OutOfRangeShopCount);

public sealed record UpdatePlatformSettingsCommand(
    Guid AdminUserId,
    string? CommissionPaymentIban,
    string? CommissionPaymentBankName,
    string? CommissionPaymentAccountHolderName,
    decimal DepositPercentMin,
    decimal DepositPercentMax) : ICommand<UpdatePlatformSettingsResponse>;

public sealed class UpdatePlatformSettingsCommandValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        // Sıkı aralık denetimi (alt < üst, ikisi de %0-%100 içinde) domain'de
        // PlatformSettings.UpdateDepositPercentBounds() içinde yapılıyor.
        RuleFor(x => x.DepositPercentMin).GreaterThan(0);
        RuleFor(x => x.DepositPercentMax).GreaterThan(0);
    }
}

public sealed class UpdatePlatformSettingsCommandHandler(
    IApplicationDbContext db, IAuditLogger audit, ILogger<UpdatePlatformSettingsCommandHandler> logger)
    : IRequestHandler<UpdatePlatformSettingsCommand, Result<UpdatePlatformSettingsResponse>>
{
    public async Task<Result<UpdatePlatformSettingsResponse>> Handle(UpdatePlatformSettingsCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var settings = await ctx.Set<PlatformSettings>().FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure<UpdatePlatformSettingsResponse>(Error.NotFound("PlatformSettings"));

        var bankingResult = settings.UpdateBankingInfo(
            request.CommissionPaymentIban, request.CommissionPaymentBankName, request.CommissionPaymentAccountHolderName);
        if (bankingResult.IsFailure) return Result.Failure<UpdatePlatformSettingsResponse>(bankingResult.Error);

        var depositBoundsResult = settings.UpdateDepositPercentBounds(request.DepositPercentMin, request.DepositPercentMax);
        if (depositBoundsResult.IsFailure) return Result.Failure<UpdatePlatformSettingsResponse>(depositBoundsResult.Error);

        // RFC-013 review I1: yeni aralık daralınca aralık dışında kalan (DepositEnabled=true
        // VE DepositPercent yeni [min,max] dışında) shop'ları SESSİZCE değiştirmeyiz —
        // yalnız say + audit'e yaz + logla, admin görünürlük kazansın.
        var outOfRangeShopCount = await ctx.Set<BarberShop>().AsNoTracking()
            .CountAsync(s => s.BookingPolicyDepositEnabled
                && s.BookingPolicyDepositPercent != null
                && (s.BookingPolicyDepositPercent < request.DepositPercentMin || s.BookingPolicyDepositPercent > request.DepositPercentMax), ct);

        if (outOfRangeShopCount > 0)
        {
            logger.LogWarning(
                "UpdatePlatformSettings: {Count} shop'un DepositPercent'i yeni [{Min},{Max}] aralığı dışında kaldı (otomatik clamp YOK, owner bir sonraki PUT'ta uymak zorunda).",
                outOfRangeShopCount, request.DepositPercentMin, request.DepositPercentMax);
        }

        await audit.RecordAsync(
            "admin.platform_settings.updated",
            nameof(PlatformSettings),
            settings.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new
            {
                hasIban = !string.IsNullOrWhiteSpace(request.CommissionPaymentIban),
                request.DepositPercentMin,
                request.DepositPercentMax,
                outOfRangeShopCount,
            },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success(new UpdatePlatformSettingsResponse(outOfRangeShopCount));
    }
}
