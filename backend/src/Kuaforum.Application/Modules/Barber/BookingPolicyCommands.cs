using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuaforum.Application.Modules.Barber;

// RFC-013 C3: owner'ın kendi işletmesi için kapora + anti-spam ayarı. Global
// Payment:OnlinePaymentsEnabled (RFC-009 master-switch) kapalıyken DepositEnabled=true
// KAYDEDİLEBİLİR (owner ilerisi için ayarını önceden hazırlayabilir) ama etkisizdir —
// EffectiveDepositActive bu ikisinin (global flag && DepositEnabled) AND'i.

public sealed record BookingPolicyDto(
    bool DepositEnabled,
    decimal? DepositPercent,
    bool EffectiveDepositActive,
    int MaxPendingPerCustomer,
    int? MinDaysBetweenBookings,
    int? MaxBookingsPerMonth,
    decimal DepositPercentMin,
    decimal DepositPercentMax);

public sealed record GetBookingPolicyQuery(Guid OwnerId) : IQuery<BookingPolicyDto>;

public sealed class GetBookingPolicyQueryHandler(IApplicationDbContext db, IOptions<PaymentOptions> paymentOptions)
    : IRequestHandler<GetBookingPolicyQuery, Result<BookingPolicyDto>>
{
    public async Task<Result<BookingPolicyDto>> Handle(GetBookingPolicyQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerId, ct);
        if (shop is null) return Result.Failure<BookingPolicyDto>(Error.NotFound("BarberShop"));

        var settings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure<BookingPolicyDto>(Error.NotFound("PlatformSettings"));

        var effectiveDepositActive = paymentOptions.Value.OnlinePaymentsEnabled && shop.BookingPolicyDepositEnabled;

        return Result.Success(new BookingPolicyDto(
            shop.BookingPolicyDepositEnabled,
            shop.BookingPolicyDepositPercent,
            effectiveDepositActive,
            shop.BookingPolicyMaxPendingPerCustomer,
            shop.BookingPolicyMinDaysBetweenBookings,
            shop.BookingPolicyMaxBookingsPerMonth,
            settings.DepositPercentMin,
            settings.DepositPercentMax));
    }
}

public sealed record UpdateBookingPolicyCommand(
    Guid OwnerId,
    bool DepositEnabled,
    decimal? DepositPercent,
    int MaxPendingPerCustomer,
    int? MinDaysBetweenBookings,
    int? MaxBookingsPerMonth) : ICommand<BookingPolicyDto>;

public sealed class UpdateBookingPolicyCommandValidator : AbstractValidator<UpdateBookingPolicyCommand>
{
    public UpdateBookingPolicyCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.MaxPendingPerCustomer).GreaterThan(0);
        // Aralık üst sınırları (platform tavanı, 1..365, 1..100) domain'de BookingPolicy.Create()
        // içinde denetleniyor — burada yalnız FluentValidation'ın standart "boş değer" kontrolleri.
        RuleFor(x => x.MinDaysBetweenBookings).GreaterThan(0).When(x => x.MinDaysBetweenBookings.HasValue);
        RuleFor(x => x.MaxBookingsPerMonth).GreaterThan(0).When(x => x.MaxBookingsPerMonth.HasValue);
    }
}

public sealed class UpdateBookingPolicyCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit,
    IOptions<PaymentOptions> paymentOptions) : IRequestHandler<UpdateBookingPolicyCommand, Result<BookingPolicyDto>>
{
    public async Task<Result<BookingPolicyDto>> Handle(UpdateBookingPolicyCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.OwnerId == request.OwnerId, ct);
        if (shop is null) return Result.Failure<BookingPolicyDto>(Error.NotFound("BarberShop"));

        var settings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure<BookingPolicyDto>(Error.NotFound("PlatformSettings"));

        var policyResult = BookingPolicy.Create(
            request.DepositEnabled,
            request.DepositPercent,
            request.MaxPendingPerCustomer,
            request.MinDaysBetweenBookings,
            request.MaxBookingsPerMonth,
            settings.DepositPercentMin,
            settings.DepositPercentMax);
        if (policyResult.IsFailure) return Result.Failure<BookingPolicyDto>(policyResult.Error);

        var setResult = shop.SetBookingPolicy(policyResult.Value);
        if (setResult.IsFailure) return Result.Failure<BookingPolicyDto>(setResult.Error);

        await audit.RecordAsync(
            "barber.shop.booking_policy.updated",
            nameof(BarberShop),
            shop.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new
            {
                request.DepositEnabled,
                request.DepositPercent,
                request.MaxPendingPerCustomer,
                request.MinDaysBetweenBookings,
                request.MaxBookingsPerMonth,
            },
            ct: ct);

        await db.SaveChangesAsync(ct);

        var effectiveDepositActive = paymentOptions.Value.OnlinePaymentsEnabled && shop.BookingPolicyDepositEnabled;
        return Result.Success(new BookingPolicyDto(
            shop.BookingPolicyDepositEnabled,
            shop.BookingPolicyDepositPercent,
            effectiveDepositActive,
            shop.BookingPolicyMaxPendingPerCustomer,
            shop.BookingPolicyMinDaysBetweenBookings,
            shop.BookingPolicyMaxBookingsPerMonth,
            settings.DepositPercentMin,
            settings.DepositPercentMax));
    }
}
