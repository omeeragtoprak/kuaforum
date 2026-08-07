using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
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

public sealed class AddStylistCommandHandler(IApplicationDbContext db, IAuditLogger audit, IEntitlementService entitlements)
    : IRequestHandler<AddStylistCommand, Result<Guid>>
{
    private static readonly TimeOnly DefaultOpen = new(9, 0);
    private static readonly TimeOnly DefaultClose = new(19, 0);

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

        foreach (var dow in new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        })
        {
            var isClosed = dow == DayOfWeek.Sunday;
            var whResult = WorkingHours.Create(stylist.Id, dow, DefaultOpen, DefaultClose, isClosed);
            if (whResult.IsSuccess) ctx.Set<WorkingHours>().Add(whResult.Value);
        }

        await audit.RecordAsync("barber.stylist.added", nameof(Stylist), stylist.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { request.DisplayName }, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(stylist.Id);
    }
}

public sealed record UpdateStylistCommand(
    Guid OwnerId,
    Guid StylistId,
    string? DisplayName,
    string? PhotoUrl,
    string? Bio,
    bool? IsActive) : ICommand;

public sealed class UpdateStylistCommandHandler(IApplicationDbContext db, IEntitlementService entitlements) : IRequestHandler<UpdateStylistCommand, Result>
{
    public async Task<Result> Handle(UpdateStylistCommand request, CancellationToken cancellationToken)
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

        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu stilistin bağlı olduğu işletmenin sahibi siz değilsiniz."));

        var update = stylist.UpdateProfile(request.DisplayName, request.PhotoUrl, request.Bio);
        if (update.IsFailure) return update;

        if (request.IsActive.HasValue)
        {
            // Yalnızca PASİF→AKTİF geçişi kotaya tabi (yeni bir "koltuk" işgal ediyor).
            // Zaten aktif bir stylist'i tekrar true'ya set etmek veya aktifi pasife çekmek
            // kotayı DEĞİŞTİRMEZ — kontrol yalnız gerçek geçişte çalışır, aksi halde DB'deki
            // "hâlâ aktif" satır kendi kendini sayıp sınırda olan owner'ı yanlışlıkla engeller.
            if (request.IsActive.Value && !stylist.IsActive)
            {
                var limitCheck = await StylistLimitPolicy.EnforceMaxStaffAsync(ctx, entitlements, shop.Id, cancellationToken);
                if (limitCheck.IsFailure) return limitCheck;
            }

            stylist.SetActive(request.IsActive.Value);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
