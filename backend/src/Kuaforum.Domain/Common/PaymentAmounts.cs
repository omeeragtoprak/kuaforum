namespace Kuaforum.Domain.Common;

/// <summary>
/// RFC-006 tutar hesapları. Deposit (kapora) her zaman YUKARI (ceiling) yuvarlanır.
///
/// RFC-013 M3 (adversarial review): bu XML doc önceden "DepositRate >= CommissionRate
/// invariantıyla birleşince kapora tutarı komisyonu her zaman karşılar" diyordu — bu invariant
/// RFC-012 ile CommissionRate ödeme anında sıfıra indiği için (platformCommissionMinor artık
/// InitiatePaymentCommand'da hard-code 0) anlamsızlaştı ve kaldırıldı (bkz. PaymentOptions.
/// DepositRate XML doc, Infrastructure.DependencyInjection.AddInfrastructure). Deposit artık
/// yalnızca per-shop shop.BookingPolicy.DepositPercent'ten hesaplanır; platform komisyonuyla
/// hiçbir ilişkisi yok.
///
/// RFC-014 §2 D1: <see cref="CalculateCommissionMinor"/> artık gerçek bir faturayı BESLEMİYOR —
/// SubscriptionInvoice tutarı doğrudan SubscriptionPlan.PriceMinor'dır (bkz. SubscriptionInvoice.
/// Create), oran×matrah hesabı yok. Bu metod yalnızca Booking.PlatformCommissionMinor (vestigial
/// alan, hiçbir faturaya taşınmaz — bkz. Booking.cs XML doc, InitiatePaymentCommand'ın
/// platformCommissionMinor'ı sabit 0 göndermesi) için PaymentOptions.CommissionRate config'i
/// üzerinden hâlâ çağrılıyor; kaldırılmadı çünkü Booking aggregate'i ve ona bağlı pin testleri bu
/// paketin kapsamı dışında.
///
/// Precondition: totalMinor >= 0 (negatifte ceil/round yön değiştirir).
/// </summary>
public static class PaymentAmounts
{
    public static long CalculateDepositMinor(long totalMinor, decimal depositRate)
        => (long)Math.Ceiling(totalMinor * depositRate);

    public static long CalculateCommissionMinor(long totalMinor, decimal commissionRate)
        => (long)Math.Round(totalMinor * commissionRate, MidpointRounding.AwayFromZero);
}
