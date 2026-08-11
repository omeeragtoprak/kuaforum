using Kuaforum.Domain.Common;

namespace Kuaforum.Api.Common;

/// <summary>
/// Result&lt;T&gt; → HTTP yanıtı için TEK eşleme noktası.
///
/// NEDEN VAR (G19 denetiminin asıl bulgusu, 2026-08-11)
/// ----------------------------------------------------
/// Her uç kendi ad-hoc eşlemesini elle yazıyordu. 108 `Results.Problem` çağrısının
/// dağılımı denetimde şöyle çıktı:
///
///   • 103'ü "forbidden" kodunu HİÇ bilmiyordu → sahiplik reddi 400 dönüyordu.
///     Bildirilen kusur (G19) buydu: istemciye "gönderdiğin veri bozuk" denir,
///     doğrusu "bu kaynağa yetkin yok"tur. İzlemede de yetki ihlalleri doğrulama
///     gürültüsünün içinde kaybolur.
///
///   • 24'ü `statusCode` parametresini HİÇ vermiyordu. ASP.NET Core'da
///     `Results.Problem(detail)` varsayılanı 500'dür — yani sıradan bir kullanıcı
///     hatası ("bu saat dolu", "kod yanlış") istemciye SUNUCU HATASI olarak
///     dönüyordu. Bu G19'dan daha ağır: mobil istemci 5xx'i geçici arıza sayıp
///     "tekrar dene" gösteriyor (bkz. mobile/src/lib/classify-error.ts), Sentry
///     gerçek olmayan alarm üretiyor, ve asıl hata mesajı kullanıcıya ulaşmıyor.
///     Bu, denetimin aradığı şey DEĞİLDİ; eşleme dağınıklığını tararken çıktı.
///
/// Kural: uçlar artık statusCode HESAPLAMAZ. Hata türünü Application/Domain katmanı
/// `Error.X()` fabrikasıyla söyler, HTTP karşılığı YALNIZ burada durur. Yeni bir hata
/// kodu eklenirse eşlemesi de buraya eklenir — uçlara dağılmaz.
/// </summary>
public static class ResultHttp
{
    /// <summary>
    /// Hata kodu → HTTP durum kodu. Kodlar `Domain.Common.Error` fabrikalarında üretilir.
    ///
    /// `server_misconfigured` bilerek 500: bu bir kullanıcı hatası değil, deploy/seed
    /// sırasının bozuk olduğunun işaretidir (bkz. Error.ServerMisconfigured özeti).
    /// İstemcinin düzeltebileceği bir şey yok, alarm üretmesi DOĞRU.
    /// </summary>
    public static int StatusFor(Error error) => error.Code switch
    {
        "not_found" => StatusCodes.Status404NotFound,
        "forbidden" => StatusCodes.Status403Forbidden,
        "unauthorized" => StatusCodes.Status401Unauthorized,
        "conflict" => StatusCodes.Status409Conflict,
        "server_misconfigured" => StatusCodes.Status500InternalServerError,
        "validation" => StatusCodes.Status400BadRequest,
        // Bilinmeyen kod: 400. 500'e düşürmek yanlış olurdu — bilmediğimiz bir kod
        // büyük olasılıkla yeni eklenmiş bir kullanıcı hatasıdır, sunucu arızası değil.
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// Hatayı ProblemDetails'e çevirir. `title` alanına makine-okunur hata kodu yazılır
    /// (uçların bir kısmı zaten böyle yapıyordu); `detail` kullanıcıya gösterilebilir
    /// Türkçe mesajdır. İstemci ayrımı status koduyla yapar — metin parse ETMEZ.
    /// </summary>
    public static IResult Problem(Error error) =>
        Results.Problem(detail: error.Message, statusCode: StatusFor(error), title: error.Code);

    /// <summary>
    /// Başarısız bir Result'ı ProblemDetails'e çevirir. Uçlardaki
    /// `... ? Results.Ok(x) : Results.Problem(r.Error.Message, statusCode: 400)`
    /// kalıbının hata dalı bununla değiştirilir — başarı dalı olduğu gibi kalır,
    /// dönüşüm bu yüzden mekanik ve düşük riskli.
    /// Başarılı bir Result'ta çağrılmaz; çağrılırsa Error.None ile 400 üretir.
    /// </summary>
    public static IResult ToProblem(this Result result) => Problem(result.Error);

    // --- Result (değersiz) ---

    /// <summary>Başarılıysa 204, değilse kodu eşlenmiş ProblemDetails.</summary>
    public static IResult NoContentOrProblem(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    /// <summary>Başarılıysa 200 + gövdesiz `Results.Ok()`, değilse ProblemDetails.</summary>
    public static IResult OkOrProblem(this Result result) =>
        result.IsSuccess ? Results.Ok() : Problem(result.Error);

    // --- Result<T> ---

    /// <summary>Başarılıysa 200 + değer, değilse ProblemDetails.</summary>
    public static IResult OkOrProblem<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    /// <summary>
    /// Başarılıysa 200 + <paramref name="project"/> ile şekillendirilmiş gövde.
    /// Uçların `Results.Ok(new { id = r.Value })` gibi sarmalamalarını korur.
    /// </summary>
    public static IResult OkOrProblem<T>(this Result<T> result, Func<T, object> project) =>
        result.IsSuccess ? Results.Ok(project(result.Value)) : Problem(result.Error);

    /// <summary>Başarılıysa 204, değilse ProblemDetails (değer üretip yok sayan uçlar için).</summary>
    public static IResult NoContentOrProblem<T>(this Result<T> result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    /// <summary>
    /// Başarılıysa 201 + Location, değilse ProblemDetails.
    /// Location üretimi değere bağlı olduğu için lambda alır.
    /// </summary>
    public static IResult CreatedOrProblem<T>(this Result<T> result, Func<T, string> location, Func<T, object> body) =>
        result.IsSuccess ? Results.Created(location(result.Value), body(result.Value)) : Problem(result.Error);
}
