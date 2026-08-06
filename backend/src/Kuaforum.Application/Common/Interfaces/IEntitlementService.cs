namespace Kuaforum.Application.Common.Interfaces;

/// <summary>
/// RFC-014 §2 D1: bir salonun mevcut abonelik planına göre tipli özellik sınırları.
/// MaxStaff/MaxMonthlyBookings null = sınırsız (bkz. IEntitlementService implementasyonundaki
/// "-1" sentinel parse kuralı) — DEĞERLER her zaman DB'den (plan_entitlements) gelir, burada
/// hiçbir sabit sayı yoktur.
/// </summary>
public sealed record ShopEntitlements(
    int? MaxStaff,
    int? MaxMonthlyBookings,
    bool CanUseSms,
    bool CanUseDeposit);

public interface IEntitlementService
{
    Task<ShopEntitlements> GetAsync(Guid barberShopId, CancellationToken ct = default);
}
