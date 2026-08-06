namespace Kuaforum.Domain.Modules.Billing;

/// <summary>
/// RFC-014 §2 D1 kritik ilke: "özellik sınırları koda gömülmez, DB'den (plan_entitlements)
/// okunur." Bu sabitler yalnızca entitlement anahtar İSİMLERİNİ sabitler (magic string
/// önleme) — DEĞERLER hiçbir zaman burada yaşamaz, yalnızca <see cref="PlanEntitlement"/>
/// satırlarında. Yeni bir limit eklerken buraya bir const eklemek yeterli; seed/okuma tarafı
/// (SubscriptionPlanSeeder / EntitlementService) buradan referans alır.
/// </summary>
public static class EntitlementKeys
{
    public const string MaxStaff = "max_staff";
    public const string MaxMonthlyBookings = "max_monthly_bookings";
    public const string CanUseSms = "can_use_sms";
    public const string CanUseDeposit = "can_use_deposit";
}

/// <summary>RFC-014 §2 D1: V1 launch'ta yalnız LaunchFree aktif; Starter/Pro fiyatlandırma V2'de netleşince IsActive=true'ya çekilecek.</summary>
public static class SubscriptionPlanCodes
{
    public const string LaunchFree = "LAUNCH_FREE";
    public const string Starter = "STARTER";
    public const string Pro = "PRO";
}
