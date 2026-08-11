using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record CreateBarberShopCommand(
    Guid OwnerId,
    string Name,
    SalonGenderType GenderType,
    string? Description,
    string? Phone) : ICommand<Guid>;

public sealed class CreateBarberShopCommandValidator : AbstractValidator<CreateBarberShopCommand>
{
    public CreateBarberShopCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.GenderType).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class CreateBarberShopCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<CreateBarberShopCommand, Result<Guid>>
{
    private const string DuplicateShopMessage =
        "Bu kullanıcının zaten bir işletmesi var. Çoklu işletme desteği V1.5'te gelecek.";

    public async Task<Result<Guid>> Handle(CreateBarberShopCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var owner = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.OwnerId, cancellationToken);
        if (owner is null) return Result.Failure<Guid>(Error.NotFound("Owner"));

        var existing = await ctx.Set<BarberShop>().AnyAsync(s => s.OwnerId == request.OwnerId, cancellationToken);
        if (existing) return Result.Failure<Guid>(Error.Conflict(DuplicateShopMessage));

        // RFC-007: self-serve onboarding — Customer, işletme oluştururken AYNI
        // SaveChanges içinde BarberOwner'a terfi eder (atomik rol geçişi).
        if (owner.Role == UserRole.Customer)
        {
            var promote = owner.PromoteToBarberOwner();
            if (promote.IsFailure) return Result.Failure<Guid>(promote.Error);
        }
        else if (owner.Role != UserRole.BarberOwner)
        {
            return Result.Failure<Guid>(Error.Forbidden("Bu hesap türü işletme oluşturamaz."));
        }

        var createResult = BarberShop.Create(request.OwnerId, request.Name, request.GenderType);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        var shop = createResult.Value;
        var update = shop.UpdateBasics(name: null, description: request.Description, genderType: null, phone: request.Phone);
        if (update.IsFailure) return Result.Failure<Guid>(update.Error);

        ctx.Set<BarberShop>().Add(shop);

        // RFC-014 §2 D1: aboneliksiz salon hiç oluşmamalı — yeni salon atomik olarak
        // (aynı SaveChanges) LAUNCH_FREE'ye bağlanır. Bkz. ShopSubscription.CreateLaunchFreeDefault
        // XML doc — SubscriptionPlanSeeder'ın backfill adımıyla aynı sentinel tanımını paylaşır.
        ctx.Set<ShopSubscription>().Add(ShopSubscription.CreateLaunchFreeDefault(shop.Id));

        // GAPS.md G5: solo-kuaför (TR pazarında en yaygın segment) onboarding'i "salon oluştur"
        // adımında biter — mobilde ayrıca "+ Yeni çalışan" eklemeyi UNUTURSA salon StylistCount=0
        // ile Active olur, arama sonuçlarında görünür ama randevu alınamaz (sessiz churn).
        // Çözüm: owner, kendi salonunun ilk (aktif) Stylist'i olarak AYNI SaveChanges'te eklenir —
        // sonradan owner isterse pasifleştirir/adını düzenler (UpdateStylistCommand zaten var).
        //
        // Entitlement kararı: StylistLimitPolicy.EnforceMaxStaffAsync BİLEREK burada ÇAĞRILMIYOR.
        // Sebep (1) LAUNCH_FREE MaxStaff=20 — bu şu an oluşan İLK çalışan olduğu için limite
        // hiçbir zaman gerçekten çarpamaz (1 < 20). Sebep (2) EntitlementService.GetAsync,
        // ShopSubscription'ı AsNoTracking ile DOĞRUDAN DB'den okur; yukarıdaki ShopSubscription.Add
        // henüz SaveChangesAsync çağrılmadığı için görünmez olurdu → her salon oluşturmada
        // "ShopSubscription satırı bulunamadı" sahte veri-tutarsızlığı LogError'u üretir (gürültü,
        // gerçek sinyali boğar). Sebep (3) — bozuk/eksik bir entitlement satırı yüzünden salon
        // oluşturmanın TAMAMEN kilitlenmesi kabul edilemez: yeni işletme kaydı platformun en kritik
        // huniidir, bir billing veri tutarsızlığı bunu asla bloklamamalı. Riski kabul edilebilir
        // kılan: limit her zaman ≥1 (bkz. SubscriptionPlanSeeder, tüm planlar MaxStaff ≥ 5) ve bu
        // yalnız TEK bir stylist ekliyor.
        // Stylist.DisplayName sınırı 80 karakter, BarberShop.Name sınırı 120 — shop.Name fallback'i
        // 80'i aşarsa Stylist.Create başarısız olur ve owner (yalnızca uzun bir işletme adı seçtiği
        // için) hiç salon oluşturamaz. Bu kabul edilemez: onboarding hunisi bir isim-uzunluğu
        // uyumsuzluğu yüzünden asla bloklanmamalı — 80 karaktere kırpıyoruz.
        var fallbackName = string.IsNullOrWhiteSpace(owner.DisplayName) ? shop.Name : owner.DisplayName!;
        var ownerDisplayName = fallbackName.Length > 80 ? fallbackName[..80].TrimEnd() : fallbackName;
        var stylistResult = Stylist.Create(shop.Id, ownerDisplayName, request.OwnerId);
        if (stylistResult.IsFailure) return Result.Failure<Guid>(stylistResult.Error);

        var ownerStylist = stylistResult.Value;
        ctx.Set<Stylist>().Add(ownerStylist);

        // Çalışma saati kararı: boş bırakılmıyor. Stylist var ama WorkingHours yoksa
        // GetAvailableSlots hiçbir slot üretmez ve salon yine "randevu alınamaz" kalır — bu G5'in
        // tam olarak önlemeye çalıştığı sessiz yarım-çözüm. AddStylistCommandHandler'la aynı
        // varsayılan (Pzt-Cmt 09:00-19:00, Pazar kapalı) DefaultWorkingHoursFactory'den paylaşılır.
        foreach (var wh in DefaultWorkingHoursFactory.CreateWeekFor(ownerStylist.Id))
            ctx.Set<WorkingHours>().Add(wh);

        await audit.RecordAsync("barber.shop.created", nameof(BarberShop), shop.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { shop.Name, shop.GenderType, RoleAfter = owner.Role.ToString() }, ct: cancellationToken);

        await audit.RecordAsync("barber.stylist.auto_added_owner", nameof(Stylist), ownerStylist.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { ownerDisplayName, ShopId = shop.Id }, ct: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Çift-submit yarışında kaybeden taraf, kazananın aynı SaveChanges'te commit'lediği
            // User.Version bump'ına (rol terfisi) takılabilir — 500 yerine 409.
            return Result.Failure<Guid>(Error.Conflict(DuplicateShopMessage));
        }
        catch (DbUpdateException ex) when (PostgresErrorClassifier.IsUniqueViolation(ex, out var constraint))
        {
            // AnyAsync precheck'ini yarışta atlatan ikinci istek ux_barber_shops_owner'a çarpar.
            return Result.Failure<Guid>(Error.Conflict(constraint == "ux_barber_shops_slug"
                ? "Bu isimde bir işletme zaten kayıtlı. Lütfen farklı bir ad deneyin."
                : DuplicateShopMessage));
        }

        return Result.Success(shop.Id);
    }
}
