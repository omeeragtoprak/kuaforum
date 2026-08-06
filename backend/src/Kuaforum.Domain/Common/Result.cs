namespace Kuaforum.Domain.Common;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    // Türkçeleştirilmiş entity → "X bulunamadı." dönüşü.
    // Fallback: caller ad-hoc tam cümle geçerse (boşluk/nokta içeriyor) suffix eklemez,
    // mesajı olduğu gibi kullanır. Böylece "Booking" → "Randevu bulunamadı." OLURKEN
    // "No admin user found. AdminSeeder must run first." → aynen kalır (double-suffix yok).
    public static Error NotFound(string entity)
    {
        if (string.IsNullOrWhiteSpace(entity))
            return new("not_found", "Kayıt bulunamadı.");
        if (entity.Contains(' ') || entity.EndsWith('.'))
            return new("not_found", entity);
        var tr = EntityNameTr.GetValueOrDefault(entity, entity);
        return new("not_found", $"{tr} bulunamadı.");
    }

    public static Error Validation(string message) => new("validation", message);
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Forbidden(string message) => new("forbidden", message);
    public static Error Unauthorized(string message = "Yetkisiz erişim.") => new("unauthorized", message);

    /// <summary>
    /// Follow-up (review, BookingPolicyCommands): bir kullanıcının KENDİ kaynağı eksikken
    /// ("BarberShop bulunamadı") dönen "not_found" ile, platform-seviyesi bir ayar satırı
    /// (PlatformSettings tekil satırı) eksikken dönenin AYNI koda düşmemesi için. İkisi de
    /// mesaj metniyle ayrılıyordu ama kod aynıydı ("not_found") — client bu ayrımı yalnızca
    /// Türkçe mesaj metnini parse ederek yapabiliyordu (kırılgan). Bu bir kullanıcı hatası
    /// DEĞİL — deploy/seed sırası bozulmuş demektir (bkz. PlatformSettingsSeeder), 404 değil
    /// 500 olarak map'lenir (BarberEndpoints.GetBookingPolicy/UpdateBookingPolicy).
    /// </summary>
    public static Error ServerMisconfigured(string message) => new("server_misconfigured", message);

    private static readonly Dictionary<string, string> EntityNameTr = new(StringComparer.Ordinal)
    {
        ["Booking"] = "Randevu",
        ["User"] = "Kullanıcı",
        ["OTP"] = "Doğrulama kodu",
        ["Session"] = "Oturum",
        ["BarberShop"] = "Kuaför",
        ["Customer"] = "Müşteri",
        ["Stylist"] = "Çalışan",
        ["Owner"] = "İşletme sahibi",
        ["Payment"] = "Ödeme",
        ["Review"] = "Değerlendirme",
        ["Notification"] = "Bildirim",
        ["KycSubmission"] = "KYC başvurusu",
        ["KycDocument"] = "KYC belgesi",
        ["Service"] = "Hizmet",
        ["DeviceToken"] = "Cihaz",
        ["Address"] = "Adres",
        ["Refund"] = "İade",
        ["TimeOff"] = "İzin",
        ["WorkingHours"] = "Çalışma saati",
    };
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Successful result cannot have an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failed result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default!, false, error);
}

public sealed class Result<T> : Result
{
    private readonly T _value;

    internal Result(T value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public static implicit operator Result<T>(T value) => Success(value);
}
