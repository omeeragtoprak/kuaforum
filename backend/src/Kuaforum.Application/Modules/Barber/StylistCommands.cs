using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record AddStylistCommand(
    Guid OwnerId,
    Guid BarberShopId,
    string DisplayName,
    Guid? UserId) : ICommand<Guid>;

public sealed class AddStylistCommandValidator : AbstractValidator<AddStylistCommand>
{
    public AddStylistCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
    }
}

/// <summary>
/// RFC-014 §2 D1 review fix: max_staff kotası hem yeni çalışan EKLERKEN (AddStylistCommandHandler)
/// hem de pasif bir çalışanı YENİDEN AKTİFLEŞTİRİRKEN (UpdateStylistCommandHandler'ın
/// SetActive(true) dalı) aynı kurala tabi olmalı — ikinci yol daha önce kontrolsüzdü: owner
/// limitteyken pasif bir stylist'i aktifleştirerek kotayı sessizce aşabiliyordu. Tek ortak
/// kaynak burada; iki handler da bunu çağırır.
/// </summary>
internal static class StylistLimitPolicy
{
    public static async Task<Result> EnforceMaxStaffAsync(
        DbContext ctx, IEntitlementService entitlements, Guid barberShopId, CancellationToken ct)
    {
        var plan = await entitlements.GetAsync(barberShopId, ct);
        if (!plan.MaxStaff.HasValue) return Result.Success();

        var activeStaffCount = await ctx.Set<Stylist>()
            .CountAsync(s => s.BarberShopId == barberShopId && s.IsActive, ct);
        if (activeStaffCount >= plan.MaxStaff.Value)
        {
            return Result.Failure(Error.Validation(
                $"Mevcut abonelik planınız en fazla {plan.MaxStaff.Value} aktif çalışan eklemenize izin veriyor. Daha fazla çalışan eklemek için planınızı yükseltmeniz gerekiyor."));
        }

        return Result.Success();
    }
}

/// <summary>
/// G5: Pazartesi-Cumartesi 09:00-19:00, Pazar kapalı — yeni bir stylist'in gördüğü ilk çalışma
/// saati varsayılanı. Owner elle "+ Yeni çalışan" eklerken (AddStylistCommandHandler) VE
/// CreateBarberShopCommandHandler salonun oto-eklenen ilk stylist'i (owner'ın kendisi) için
/// AYNI varsayılanı kullanır — yalnız Stylist satırı eklemek yetmez, WorkingHours olmadan
/// GetAvailableSlots hiç slot üretmez ve salon "randevu alınabilir" görünmez (bkz. GAPS.md G5).
/// Tek kaynak burada; iki handler da bunu çağırır.
/// </summary>
internal static class DefaultWorkingHoursFactory
{
    public static readonly TimeOnly OpenTime = new(9, 0);
    public static readonly TimeOnly CloseTime = new(19, 0);

    public static IEnumerable<WorkingHours> CreateWeekFor(Guid stylistId)
    {
        foreach (var dow in new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        })
        {
            var isClosed = dow == DayOfWeek.Sunday;
            var result = WorkingHours.Create(stylistId, dow, OpenTime, CloseTime, isClosed);
            if (result.IsSuccess) yield return result.Value;
        }
    }
}

public sealed class AddStylistCommandHandler(IApplicationDbContext db, IAuditLogger audit, IEntitlementService entitlements)
    : IRequestHandler<AddStylistCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddStylistCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure<Guid>(Error.NotFound("BarberShop"));
        if (shop.OwnerId != request.OwnerId) return Result.Failure<Guid>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        // RFC-014 §2 D1: max_staff aktif abonelik planından okunur, koda gömülü değil.
        // Yalnızca aktif (IsActive) çalışanlar sayılır — pasif bir çalışan kotayı işgal etmemeli.
        var limitCheck = await StylistLimitPolicy.EnforceMaxStaffAsync(ctx, entitlements, shop.Id, cancellationToken);
        if (limitCheck.IsFailure) return Result.Failure<Guid>(limitCheck.Error);

        var createResult = Stylist.Create(request.BarberShopId, request.DisplayName, request.UserId);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        var stylist = createResult.Value;
        ctx.Set<Stylist>().Add(stylist);

        foreach (var wh in DefaultWorkingHoursFactory.CreateWeekFor(stylist.Id))
            ctx.Set<WorkingHours>().Add(wh);

        await audit.RecordAsync("barber.stylist.added", nameof(Stylist), stylist.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { request.DisplayName }, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(stylist.Id);
    }
}

/// <summary>RFC-015 §2 D2: pasifleştirme yanıtı — bu çağrı Aktif→Pasif geçişini FİİLEN
/// tetiklediyse o stylist'in etkilenen (gelecekteki Pending/Confirmed) randevularını taşır.
/// Diğer tüm durumlarda (aktifleştirme, profil-only güncelleme, no-op tekrar) boş liste döner —
/// "etkilenen" yalnız gerçek bir aktif→pasif geçişte anlamlı.</summary>
public sealed record UpdateStylistResultDto(IReadOnlyList<AffectedBookingDto> AffectedBookings);

public sealed record UpdateStylistCommand(
    Guid OwnerId,
    Guid StylistId,
    string? DisplayName,
    string? PhotoUrl,
    string? Bio,
    bool? IsActive) : ICommand<UpdateStylistResultDto>;

/// <summary>
/// RFC-015 §2 D2: pasifleştirme ENGELLENMEZ — owner devir/iptal kararını kendi verir, sistem
/// yalnız körlüğü giderir. Otomatik devretme/iptal YAPILMAZ (V1.5'e ertelendi, RFC §3). Müşteriye
/// bildirim bu maddede YOK (RFC yalnız kuaför-tarafı uyarı istiyor) — D1'in aksine burada
/// INotificationDispatcher hiç enjekte edilmiyor, kapsam dışı iş üretmemek için bilinçli.
/// </summary>
public sealed class UpdateStylistCommandHandler(IApplicationDbContext db, IEntitlementService entitlements)
    : IRequestHandler<UpdateStylistCommand, Result<UpdateStylistResultDto>>
{
    public async Task<Result<UpdateStylistResultDto>> Handle(UpdateStylistCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        // Review fix: `.Include(s => EF.Property<BarberShop>(s, "_shop"))` was invalid EF Core
        // syntax (Include lambdas must be plain property accesses; EF.Property is only valid in
        // Where/OrderBy) AND pointed at a navigation that was never configured on Stylist in the
        // first place — it threw InvalidOperationException on every single call, 500ing this
        // entire endpoint. Nothing ever caught it because no test exercised PATCH /v1/stylists/{id}
        // until this package added one (bkz. StylistMaxStaffLimitTests). The shop is already
        // fetched separately two lines below, so the Include was also dead weight — removed
        // rather than "fixed", since nothing consumed the navigation it was trying to load.
        var stylist = await ctx.Set<Stylist>()
            .FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);

        if (stylist is null) return Result.Failure<UpdateStylistResultDto>(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<UpdateStylistResultDto>(Error.Forbidden("Bu stilistin bağlı olduğu işletmenin sahibi siz değilsiniz."));

        var update = stylist.UpdateProfile(request.DisplayName, request.PhotoUrl, request.Bio);
        if (update.IsFailure) return Result.Failure<UpdateStylistResultDto>(update.Error);

        // RFC-015 §2 D2: yalnız FİİLEN aktif→pasif geçişi (wasActive true, hedef false) etkilenen
        // randevu sorgusunu tetikler — pasif kalan bir stylist'e tekrar isActive=false göndermek
        // (no-op) veya aktifleştirme, "etkilenen" kavramının hiç geçerli olmadığı durumlar.
        var isDeactivating = request.IsActive == false && stylist.IsActive;

        if (request.IsActive.HasValue)
        {
            // Yalnızca PASİF→AKTİF geçişi kotaya tabi (yeni bir "koltuk" işgal ediyor).
            // Zaten aktif bir stylist'i tekrar true'ya set etmek veya aktifi pasife çekmek
            // kotayı DEĞİŞTİRMEZ — kontrol yalnız gerçek geçişte çalışır, aksi halde DB'deki
            // "hâlâ aktif" satır kendi kendini sayıp sınırda olan owner'ı yanlışlıkla engeller.
            if (request.IsActive.Value && !stylist.IsActive)
            {
                var limitCheck = await StylistLimitPolicy.EnforceMaxStaffAsync(ctx, entitlements, shop.Id, cancellationToken);
                if (limitCheck.IsFailure) return Result.Failure<UpdateStylistResultDto>(limitCheck.Error);
            }

            stylist.SetActive(request.IsActive.Value);
        }

        await db.SaveChangesAsync(cancellationToken);

        IReadOnlyList<AffectedBookingDto> affected = Array.Empty<AffectedBookingDto>();
        if (isDeactivating)
            affected = await StylistFutureBookingsFinder.GetFutureActiveBookingsAsync(ctx, stylist.Id, cancellationToken);

        return Result.Success(new UpdateStylistResultDto(affected));
    }
}

public sealed record ToggleStylistWalkInCommand(Guid OwnerId, Guid StylistId, bool Available) : ICommand;

public sealed class ToggleStylistWalkInCommandValidator : AbstractValidator<ToggleStylistWalkInCommand>
{
    public ToggleStylistWalkInCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
    }
}

public sealed class ToggleStylistWalkInCommandHandler(IApplicationDbContext db) : IRequestHandler<ToggleStylistWalkInCommand, Result>
{
    public async Task<Result> Handle(ToggleStylistWalkInCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var toggle = stylist.ToggleWalkIn(request.Available);
        if (toggle.IsFailure) return toggle;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
