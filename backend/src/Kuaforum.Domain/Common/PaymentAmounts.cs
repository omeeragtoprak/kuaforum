namespace Kuaforum.Domain.Common;

/// <summary>
/// RFC-006 tutar hesapları. Deposit (kapora) her zaman YUKARI (ceiling) yuvarlanır. Tek doğruluk
/// kaynağı: Booking.Initiate dahil her komisyon/kapora hesabı buradan geçer.
///
/// RFC-013 M3 (adversarial review): bu XML doc önceden "DepositRate >= CommissionRate
/// invariantıyla birleşince kapora tutarı komisyonu her zaman karşılar" diyordu — bu invariant
/// RFC-012 ile CommissionRate ödeme anında sıfıra indiği için (platformCommissionMinor artık
/// InitiatePaymentCommand'da hard-code 0; gerçek komisyon ay sonu CommissionStatement'a taşındı)
/// anlamsızlaştı ve kaldırıldı (bkz. PaymentOptions.DepositRate XML doc,
/// Infrastructure.DependencyInjection.AddInfrastructure). Deposit artık yalnızca per-shop
/// shop.BookingPolicy.DepositPercent'ten hesaplanır; platform komisyonuyla hiçbir ilişkisi yok.
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
