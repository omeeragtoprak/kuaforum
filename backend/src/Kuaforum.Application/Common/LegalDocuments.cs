namespace Kuaforum.Application.Common;

/// <summary>
/// Legal metin (KVKK aydınlatma, kullanım koşulları) versiyon ve slug sabitleri.
/// Tek doğruluk kaynağı: hem <c>GET /v1/legal/{doc}</c> hem de rıza kayıtlarının
/// (<see cref="Kuaforum.Domain.Modules.Identity.Consent.Version"/>) versiyon karşılaştırması
/// buradan okur. Hukuki metin güncellendiğinde YALNIZ bu sabitler + ilgili markdown dosyası
/// değişir; bu otomatik olarak tüm kullanıcılar için rıza akışını yeniden tetikler
/// (bkz. <c>RequiresTosConsent</c> türetimi).
/// </summary>
public static class LegalDocuments
{
    public const string CurrentTosVersion = "v1";
    public const string CurrentAydinlatmaVersion = "v1";

    public const string AydinlatmaSlug = "aydinlatma";
    public const string KullanimKosullariSlug = "kullanim-kosullari";
}
