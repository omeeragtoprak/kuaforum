namespace Kuaforum.Application.Common;

/// <summary>
/// RFC-006 kapora planı: platform komisyon oranı config-driven.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>
    /// Platform komisyonu (toplam hizmet bedeli üzerinden). RFC-006'da tanımlandı ama RFC-012
    /// ile ödeme anında hiç KULLANILMIYOR — InitiatePaymentCommand platformCommissionMinor'ı
    /// sabit 0 gönderiyor, gerçek komisyon ay sonu CommissionStatement'a (Billing modülü) taşındı.
    /// Bu alan hâlâ Booking.Initiate'e (booking.PlatformCommissionMinor — vestigial, faturalama
    /// bundan beslenmiyor) geçiyor; canlı komisyon hesaplaması için tek doğruluk kaynağı artık
    /// Billing.PlatformSettings.
    /// </summary>
    public decimal CommissionRate { get; set; } = 0.10m;

    /// <summary>
    /// RFC-013 M3 (adversarial review): ARTIK KULLANILMIYOR — kapora oranı RFC-013 C3 ile
    /// per-shop'a taşındı (bkz. Kuaforum.Domain.Modules.Barber.BookingPolicy.DepositPercent,
    /// InitiatePaymentCommand'daki canlı shop.BookingPolicyDepositPercent okuması). Bu alan
    /// yalnızca appsettings.json'daki eski config anahtarının binding hatası vermemesi için
    /// geriye dönük duruyor; hiçbir kod yolu değerini okumuyor (grep ile doğrulandı). Eskiden
    /// burada "DepositRate >= CommissionRate" invariantı vardı — RFC-012 ile CommissionRate ödeme
    /// anında sıfıra indiği için invariant anlamsızlaştı ve yanlış-pozitif boot failure
    /// üretiyordu; kaldırıldı (bkz. Infrastructure.DependencyInjection.AddInfrastructure).
    /// </summary>
    public decimal DepositRate { get; set; } = 0.15m;

    /// <summary>
    /// RFC-009 ücretsiz büyüme modu: online ödeme (PSP checkout) feature-flag'i. Fail-closed
    /// default <c>false</c> — PayTR merchant hesabı bağlanana kadar randevu salonda ödenir,
    /// komisyon tahsil edilmez. <c>appsettings.Development.json</c> bunu <c>true</c>'ya override
    /// eder ki RFC-006 kapora/webhook integration test suite'i regresyonsuz çalışsın.
    /// <see cref="InitiatePaymentCommand"/> handler'ı bu false iken 409 döner; RFC-006'nın
    /// kapora+komisyon+webhook mimarisi koddan sökülmüyor, yalnızca aktivasyonu bu flag'e bağlı.
    /// </summary>
    public bool OnlinePaymentsEnabled { get; set; } = false;
}
