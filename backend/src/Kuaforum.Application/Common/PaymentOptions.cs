namespace Kuaforum.Application.Common;

/// <summary>
/// RFC-006 kapora planı — RFC-014 §2 D1 ile gelir modeli sabit aylık aboneliğe geçtiği için
/// bu sınıfta artık canlı bir "platform komisyon oranı" alanı YOK (bkz. tarihçe için
/// Infrastructure.DependencyInjection.AddInfrastructure'ın CommissionRate yorum bloğu, review
/// fix 2026-08-07: eski <c>Payment:CommissionRate</c> config anahtarı + ValidateOnStart
/// zorunluluğu, hiçbir para yoluna girmeyen bir alanı "gerekli" gibi gösterip yanıltıcı bir
/// DB kolonu (booking.platform_commission_minor) dolduruyordu — kaldırıldı).
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>
    /// RFC-013 M3 (adversarial review): ARTIK KULLANILMIYOR — kapora oranı RFC-013 C3 ile
    /// per-shop'a taşındı (bkz. Kuaforum.Domain.Modules.Barber.BookingPolicy.DepositPercent,
    /// InitiatePaymentCommand'daki canlı shop.BookingPolicyDepositPercent okuması). Bu alan
    /// yalnızca appsettings.json'daki eski config anahtarının binding hatası vermemesi için
    /// geriye dönük duruyor; hiçbir kod yolu değerini okumuyor (grep ile doğrulandı).
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
