using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber;

/// <summary>
/// RFC-013 C3: per-shop kapora ayarı + dinamik anti-spam kuralları. Value object —
/// <see cref="Address"/> (Kuaforum.Domain.Modules.Geography) kalıbıyla aynı: BarberShop
/// bunu owned-entity/ayrı tablo olarak DEĞİL, flattened scalar kolonlar olarak taşır
/// (bkz. BarberShop.BookingPolicyDepositEnabled vb. + BarberShop.SetBookingPolicy).
/// Bu sınıf yalnızca doğrulama + immutable taşıyıcı; DB'ye hiç dokunmaz.
///
/// DepositPercent sınırları (min/max) admin-ayarlı <see cref="Billing.PlatformSettings"/>'ten
/// gelir — domain katmanı DB'ye bağımlı olamayacağından Create() bu sınırları parametre
/// olarak alır (Booking.Initiate'in commissionRate'i parametre almasıyla aynı ilke).
/// </summary>
public sealed class BookingPolicy
{
    private BookingPolicy() { }

    public bool DepositEnabled { get; private set; }
    public decimal? DepositPercent { get; private set; }
    public int MaxPendingPerCustomer { get; private set; }
    public int? MinDaysBetweenBookings { get; private set; }
    public int? MaxBookingsPerMonth { get; private set; }

    /// <summary>
    /// Platform tabanı — bkz. InitiateBookingCommand.MaxActivePendingBookingsPerCustomer
    /// (Application katmanındaki GLOBAL, shop'tan bağımsız tavan). Per-shop limit bunu
    /// sıkılaştırabilir, gevşetemez; ikisi ayrı sabitler olarak duruyor (bu repo'da
    /// BarberShop.SetCommissionRateOverride / PlatformSettings.UpdateCommissionRateGlobal
    /// arasında da aynı bilinçli tekrar var — cross-layer sabit paylaşımı yok). `public`
    /// olması BİLEREK: RFC-013 review M4 — bu değeri InitiateBookingCommandHandler.
    /// MaxActivePendingBookingsPerCustomer ile eşitleyen bir pin testi var
    /// (PolicyConstantsPinTests) ki biri değişip diğeri unutulursa per-shop tavan sessizce
    /// global tabanı geçebilsin diye derleme zamanında değil ama test zamanında yakalansın.
    /// </summary>
    public const int MaxPendingPerCustomerCeiling = 3;

    private const int MinDaysBetweenBookingsCeiling = 365;
    private const int MaxBookingsPerMonthCeiling = 100;

    /// <summary>Yeni işletme / migration sonrası mevcut işletmeler için: kapora kapalı, sadece platformun varsayılan 2-Pending anti-spam sınırı açık.</summary>
    public static BookingPolicy CreateDefault() => new()
    {
        DepositEnabled = false,
        DepositPercent = null,
        MaxPendingPerCustomer = 2,
        MinDaysBetweenBookings = null,
        MaxBookingsPerMonth = null,
    };

    public static Result<BookingPolicy> Create(
        bool depositEnabled,
        decimal? depositPercent,
        int maxPendingPerCustomer,
        int? minDaysBetweenBookings,
        int? maxBookingsPerMonth,
        decimal depositPercentMin,
        decimal depositPercentMax)
    {
        // Global Payment:OnlinePaymentsEnabled=false iken bile DepositEnabled=true SET
        // EDİLEBİLİR (RFC-013 kararı) — master-switch kontrolü burada DEĞİL, Application
        // katmanında effectiveDepositActive hesaplamasında yapılır. Domain yalnızca
        // DepositPercent'in admin aralığında olup olmadığını bilir.
        if (depositEnabled)
        {
            if (!depositPercent.HasValue || depositPercent.Value < depositPercentMin || depositPercent.Value > depositPercentMax)
            {
                return Result.Failure<BookingPolicy>(Error.Validation(
                    $"Kapora oranı %{depositPercentMin * 100:0.##} ile %{depositPercentMax * 100:0.##} arasında olmalı."));
            }
        }

        if (maxPendingPerCustomer < 1 || maxPendingPerCustomer > MaxPendingPerCustomerCeiling)
        {
            return Result.Failure<BookingPolicy>(Error.Validation(
                $"Bekleyen randevu limiti 1 ile {MaxPendingPerCustomerCeiling} arasında olmalı."));
        }

        if (minDaysBetweenBookings is < 1 or > MinDaysBetweenBookingsCeiling)
        {
            return Result.Failure<BookingPolicy>(Error.Validation(
                $"Randevular arası minimum gün 1 ile {MinDaysBetweenBookingsCeiling} arasında olmalı."));
        }

        if (maxBookingsPerMonth is < 1 or > MaxBookingsPerMonthCeiling)
        {
            return Result.Failure<BookingPolicy>(Error.Validation(
                $"Aylık randevu limiti 1 ile {MaxBookingsPerMonthCeiling} arasında olmalı."));
        }

        return Result.Success(new BookingPolicy
        {
            // DepositEnabled=false iken DepositPercent anlamsız — girdi ne olursa olsun
            // null'a normalize edilir (RFC-013 tablosu: "yalnız DepositEnabled iken anlamlı").
            DepositEnabled = depositEnabled,
            DepositPercent = depositEnabled ? depositPercent : null,
            MaxPendingPerCustomer = maxPendingPerCustomer,
            MinDaysBetweenBookings = minDaysBetweenBookings,
            MaxBookingsPerMonth = maxBookingsPerMonth,
        });
    }
}
