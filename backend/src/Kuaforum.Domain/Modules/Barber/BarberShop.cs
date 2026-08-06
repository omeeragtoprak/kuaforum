using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Geography;

namespace Kuaforum.Domain.Modules.Barber;

public sealed class BarberShop : AggregateRoot
{
    private BarberShop() { }

    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? PhoneE164 { get; private set; }
    public SalonGenderType GenderType { get; private set; }
    public BarberShopStatus Status { get; private set; }

    public Guid? AddressCityId { get; private set; }
    public Guid? AddressDistrictId { get; private set; }
    public Guid? AddressNeighborhoodId { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? AddressPostalCode { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    public string? SubMerchantKey { get; private set; }
    public string? CommissionTier { get; private set; }

    /// <summary>
    /// RFC-012: null = global PlatformSettings.CommissionRateGlobal oranını miras alır.
    /// Admin-only (SetBarberShopCommissionRateOverrideCommand). CommissionTier alanına
    /// (RFC-009 yer tutucusu) dokunulmuyor — bu, gerçek bir oran mekanizması.
    /// </summary>
    public decimal? CommissionRateOverride { get; private set; }

    /// <summary>
    /// RFC-013 C3: BookingPolicy flattened kolonlar — Address kalıbıyla aynı (owned-entity
    /// yok, ayrı tablo yok). BookingPolicy VO yalnızca doğrulama/taşıyıcı; kalıcı gerçek
    /// bu alanlar. Varsayılanlar BookingPolicy.CreateDefault() ile birebir aynı olmalı.
    /// </summary>
    public bool BookingPolicyDepositEnabled { get; private set; }
    public decimal? BookingPolicyDepositPercent { get; private set; }
    public int BookingPolicyMaxPendingPerCustomer { get; private set; } = 2;
    public int? BookingPolicyMinDaysBetweenBookings { get; private set; }
    public int? BookingPolicyMaxBookingsPerMonth { get; private set; }

    public static Result<BarberShop> Create(Guid ownerId, string name, SalonGenderType genderType)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Result.Failure<BarberShop>(Error.Validation("İşletme adı gerekli ve en fazla 120 karakter olabilir."));

        return new BarberShop
        {
            OwnerId = ownerId,
            Name = name.Trim(),
            Slug = SlugHelper.Make(name),
            GenderType = genderType,
            Status = BarberShopStatus.Draft,
            CommissionTier = "standard",
        };
    }

    public Result UpdateBasics(string? name, string? description, SalonGenderType? genderType, string? phone)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                return Result.Failure(Error.Validation("İşletme adı en fazla 120 karakter olabilir."));
            Name = name.Trim();
            Slug = SlugHelper.Make(name);
        }
        if (description is not null) Description = description.Length > 2000 ? description[..2000] : description;
        if (genderType.HasValue) GenderType = genderType.Value;
        if (phone is not null) PhoneE164 = phone;

        Touch();
        return Result.Success();
    }

    public Result SetAddress(Address address)
    {
        AddressCityId = address.CityId;
        AddressDistrictId = address.DistrictId;
        AddressNeighborhoodId = address.NeighborhoodId;
        AddressLine1 = address.Line1;
        AddressLine2 = address.Line2;
        AddressPostalCode = address.PostalCode;
        Latitude = address.Coordinate.Latitude;
        Longitude = address.Coordinate.Longitude;
        Touch();
        return Result.Success();
    }

    public Result MarkPendingKyc()
    {
        if (Status != BarberShopStatus.Draft)
            return Result.Failure(Error.Validation("Yalnızca taslak durumundaki işletmeler KYC başvurusu gönderebilir."));
        Status = BarberShopStatus.PendingKyc;
        Touch();
        return Result.Success();
    }

    public Result ApproveKyc(string subMerchantKey)
    {
        if (Status != BarberShopStatus.PendingKyc)
            return Result.Failure(Error.Validation("İşletmenin KYC başvurusu beklemede değil."));
        SubMerchantKey = subMerchantKey;
        Status = BarberShopStatus.Active;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// KYC reddedildiğinde PendingKyc → Draft'a döner. MarkPendingKyc yalnızca
    /// Draft'tan geçişe izin verdiği için bu adım olmadan reddedilen bir işletme
    /// sonsuza dek PendingKyc'de kilitli kalır ve owner asla yeniden başvuramaz.
    /// </summary>
    public Result RevertToDraft()
    {
        if (Status != BarberShopStatus.PendingKyc)
            return Result.Failure(Error.Validation("Yalnızca KYC incelemesindeki işletmeler taslağa döndürülebilir."));
        Status = BarberShopStatus.Draft;
        Touch();
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (Status == BarberShopStatus.Closed)
            return Result.Failure(Error.Validation("Kapanmış işletme askıya alınamaz."));
        Status = BarberShopStatus.Suspended;
        Touch();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != BarberShopStatus.Suspended)
            return Result.Failure(Error.Validation("İşletme askıya alınmış durumda değil."));
        Status = BarberShopStatus.Active;
        Touch();
        return Result.Success();
    }

    /// <summary>RFC-012 admin aksiyonu — null geçirmek override'ı kaldırıp global orana döner.</summary>
    public Result SetCommissionRateOverride(decimal? rate)
    {
        if (rate is <= 0 or > 0.5m)
            return Result.Failure(Error.Validation("Komisyon oranı %0 ile %50 arasında olmalı."));

        CommissionRateOverride = rate;
        Touch();
        return Result.Success();
    }

    /// <summary>RFC-013 C3: owner kendi kapora + anti-spam ayarını değiştirir. Doğrulama BookingPolicy.Create() içinde yapılmış olmalı (Application katmanı çağırır).</summary>
    public Result SetBookingPolicy(BookingPolicy policy)
    {
        BookingPolicyDepositEnabled = policy.DepositEnabled;
        BookingPolicyDepositPercent = policy.DepositPercent;
        BookingPolicyMaxPendingPerCustomer = policy.MaxPendingPerCustomer;
        BookingPolicyMinDaysBetweenBookings = policy.MinDaysBetweenBookings;
        BookingPolicyMaxBookingsPerMonth = policy.MaxBookingsPerMonth;
        Touch();
        return Result.Success();
    }
}
