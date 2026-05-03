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

    public static Result<BarberShop> Create(Guid ownerId, string name, SalonGenderType genderType)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Result.Failure<BarberShop>(Error.Validation("Name is required and max 120 chars."));

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
                return Result.Failure(Error.Validation("Name max 120 chars."));
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
            return Result.Failure(Error.Validation("Only draft shops can submit KYC."));
        Status = BarberShopStatus.PendingKyc;
        Touch();
        return Result.Success();
    }

    public Result ApproveKyc(string subMerchantKey)
    {
        if (Status != BarberShopStatus.PendingKyc)
            return Result.Failure(Error.Validation("Shop is not pending KYC."));
        SubMerchantKey = subMerchantKey;
        Status = BarberShopStatus.Active;
        Touch();
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (Status == BarberShopStatus.Closed)
            return Result.Failure(Error.Validation("Closed shop cannot be suspended."));
        Status = BarberShopStatus.Suspended;
        Touch();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != BarberShopStatus.Suspended)
            return Result.Failure(Error.Validation("Shop is not suspended."));
        Status = BarberShopStatus.Active;
        Touch();
        return Result.Success();
    }
}
