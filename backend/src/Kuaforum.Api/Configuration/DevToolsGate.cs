using System.Security.Cryptography;
using System.Text;

namespace Kuaforum.Api.Configuration;

/// <summary>
/// Dev-only yüzeylerin (POST /v1/auth/dev-login, /v1/dev/*) hangi ortamlarda map edildiğini ve
/// isteğe bağlı bir paylaşılan-secret ile nasıl ek korunduğunu tek yerde toplar.
///
/// Bağlam (2026-08 — Koyeb free-tier test deploy hazırlığı): founder'ın gerçek NetGSM/PayTR
/// hesapları henüz yok, yani gerçek OTP akışıyla test edemiyor — dev-login'e ihtiyacı var. Ama
/// bu ortam internete açık bir URL (Koyeb public domain). dev-login <c>AllowAnonymous</c>'tur ve
/// body'de yalnız bir rol adı ister (bkz. DevLoginCommand — Admin dahil HERHANGİ bir rolü hiçbir
/// kimlik doğrulaması olmadan verir); bu yüzden internete açıldığında "kimliksiz Admin token"
/// riskini taşır. Mitigasyon iki katman: (1) varsayılan KAPALI bayrak (DevTools:Enabled), (2)
/// isteğe bağlı paylaşılan-secret header (DevTools:AccessKey). Üçüncü olası katman (IP allowlist)
/// BİLEREK atlandı — Koyeb free-tier'da IP-bazlı ingress kısıtı yok [unverified 2026-08-11 —
/// Koyeb'in ücretli planlarında olabilir, free tier'da doğrulanmadı] ve tek kullanıcı (founder)
/// için pratik fayda düşük. Nihai karar (bayrağı açıp açmamak, AccessKey seçip seçmemek)
/// founder'a bırakıldı — bkz. docs/DEPLOY-FREE-TIER.md.
/// </summary>
public static class DevToolsGate
{
    /// <summary>
    /// Development ortamında her zaman true (mevcut davranış, değişmedi). Development
    /// OLMAYAN ortamlarda (Production dahil) yalnız <c>DevTools:Enabled=true</c> ise true —
    /// VARSAYILAN KAPALI, bilerek açılmadıkça dev-login/dev-endpoints internete sızmaz.
    /// </summary>
    public static bool IsEnabled(IWebHostEnvironment env, IConfiguration config)
        => env.IsDevelopment() || config.GetValue<bool>("DevTools:Enabled");

    /// <summary>
    /// True ⟺ dev-only yüzeyler Development DIŞINDA bir ortamda (yani yalnız bayrakla) açık —
    /// Program.cs boot log'unda bunu açıkça uyarmak için kullanılır.
    /// </summary>
    public static bool IsEnabledOutsideDevelopment(IWebHostEnvironment env, IConfiguration config)
        => !env.IsDevelopment() && config.GetValue<bool>("DevTools:Enabled");

    /// <summary>
    /// <c>DevTools:AccessKey</c> set edilmişse, isteğin <c>X-Dev-Access-Key</c> header'ının bu
    /// değerle eşleşmesini ister (sabit-zaman karşılaştırma). AccessKey set EDİLMEMİŞSE no-op
    /// (Development'taki mevcut sürtünmesiz davranış korunur — bu, DevTools:Enabled=false iken
    /// zaten route hiç map edilmediği için pratikte anlamsız, yalnız DevTools:Enabled=true VE
    /// AccessKey boşken devreye giren bilinçli bir "ek katman yok" durumudur).
    /// </summary>
    public static IResult? CheckAccessKey(HttpContext ctx, IConfiguration config)
    {
        var expected = config["DevTools:AccessKey"];
        if (string.IsNullOrEmpty(expected))
            return null;

        var provided = ctx.Request.Headers["X-Dev-Access-Key"].ToString();
        var match = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));

        return match ? null : Results.Problem("Geçersiz veya eksik X-Dev-Access-Key.", statusCode: 403);
    }
}
