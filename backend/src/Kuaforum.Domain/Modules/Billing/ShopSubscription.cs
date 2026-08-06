using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing;

public enum ShopSubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2,
}

/// <summary>
/// RFC-014 §2 D1: her BarberShop'un TEK aboneliği (BarberShopId unique). V1'de her salon
/// LAUNCH_FREE'de ve ücretsiz — CurrentPeriodStart/End yine de tutuluyor çünkü V2'nin gerçek
/// faturalama döngüsü + otomatik yenileme job'u bu alanları kullanacak (RFC-012'nin
/// CommissionStatement dönem/DueDate desenine benzer). Aggregate root: plan değişimi ve
/// dönem ilerletme mutasyonları tek yerde, invariant'lar burada korunur.
/// </summary>
public sealed class ShopSubscription : AggregateRoot
{
    private ShopSubscription() { }

    public Guid BarberShopId { get; private set; }
    public string PlanCode { get; private set; } = null!;
    public ShopSubscriptionStatus Status { get; private set; }
    public DateTimeOffset CurrentPeriodStart { get; private set; }
    public DateTimeOffset CurrentPeriodEnd { get; private set; }

    public static Result<ShopSubscription> Create(
        Guid barberShopId,
        string planCode,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd)
    {
        if (barberShopId == Guid.Empty)
            return Result.Failure<ShopSubscription>(Error.Validation("BarberShopId gerekli."));
        if (string.IsNullOrWhiteSpace(planCode))
            return Result.Failure<ShopSubscription>(Error.Validation("Plan kodu gerekli."));
        if (currentPeriodEnd <= currentPeriodStart)
            return Result.Failure<ShopSubscription>(Error.Validation("Dönem bitişi başlangıçtan sonra olmalı."));

        return new ShopSubscription
        {
            BarberShopId = barberShopId,
            PlanCode = planCode.Trim().ToUpperInvariant(),
            Status = ShopSubscriptionStatus.Active,
            CurrentPeriodStart = currentPeriodStart,
            CurrentPeriodEnd = currentPeriodEnd,
        };
    }

    /// <summary>
    /// V1 varsayılanı: LAUNCH_FREE'nin gerçek bir dönem/yenileme mekanizması yok (herkes
    /// ücretsiz), o yüzden CurrentPeriodEnd'i 100 yıl ileri atarak "bu satır asla süresi
    /// dolmuş sayılmasın" garantisi veriyoruz. V2'de gerçek billing cycle geldiğinde bu
    /// aboneliklerin ChangePlan/AdvancePeriod ile üzerine yazılması beklenir — sentinel,
    /// "sonsuz" için kalıcı bir tasarım kararı değil, yalnızca "V1'de henüz bir süre
    /// kısıtı yok" durumunun DateTimeOffset (nullable olmayan) alanlarla ifadesi.
    /// Hem CreateBarberShopCommandHandler (yeni salon) hem de SubscriptionPlanSeeder
    /// (backfill) buradan tek bir tanımı paylaşır.
    /// </summary>
    public static ShopSubscription CreateLaunchFreeDefault(Guid barberShopId)
    {
        var now = DateTimeOffset.UtcNow;
        return Create(barberShopId, SubscriptionPlanCodes.LaunchFree, now, now.AddYears(100)).Value;
    }

    public Result ChangePlan(string newPlanCode)
    {
        if (Status == ShopSubscriptionStatus.Cancelled)
            return Result.Failure(Error.Validation("İptal edilmiş abonelikte plan değiştirilemez."));
        if (string.IsNullOrWhiteSpace(newPlanCode))
            return Result.Failure(Error.Validation("Plan kodu gerekli."));

        PlanCode = newPlanCode.Trim().ToUpperInvariant();
        Touch();
        return Result.Success();
    }

    public Result AdvancePeriod(DateTimeOffset newPeriodStart, DateTimeOffset newPeriodEnd)
    {
        if (Status == ShopSubscriptionStatus.Cancelled)
            return Result.Failure(Error.Validation("İptal edilmiş abonelikte dönem ilerletilemez."));
        if (newPeriodEnd <= newPeriodStart)
            return Result.Failure(Error.Validation("Dönem bitişi başlangıçtan sonra olmalı."));
        if (newPeriodStart < CurrentPeriodStart)
            return Result.Failure(Error.Validation("Yeni dönem mevcut dönemden geriye gidemez."));

        CurrentPeriodStart = newPeriodStart;
        CurrentPeriodEnd = newPeriodEnd;
        Touch();
        return Result.Success();
    }

    public Result MarkPastDue()
    {
        if (Status != ShopSubscriptionStatus.Active)
            return Result.Failure(Error.Validation($"{Status} durumundaki abonelik gecikmiş olarak işaretlenemez."));

        Status = ShopSubscriptionStatus.PastDue;
        Touch();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != ShopSubscriptionStatus.PastDue)
            return Result.Failure(Error.Validation("Yalnızca gecikmiş abonelikler yeniden aktifleştirilebilir."));

        Status = ShopSubscriptionStatus.Active;
        Touch();
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == ShopSubscriptionStatus.Cancelled)
            return Result.Failure(Error.Validation("Abonelik zaten iptal edilmiş."));

        Status = ShopSubscriptionStatus.Cancelled;
        Touch();
        return Result.Success();
    }
}
