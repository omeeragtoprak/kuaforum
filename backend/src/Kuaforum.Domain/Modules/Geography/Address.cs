using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Geography;

public sealed class Address
{
    private Address() { }

    public Guid CityId { get; private set; }
    public Guid DistrictId { get; private set; }

    /// <summary>
    /// RFC-007 kapanış: mahalle OPSİYONEL — TR mahalle verisi henüz tam seed'li
    /// değil (Erzurum pilot), zorunlu tutmak gerçek owner'ların adres adımını
    /// bloke ediyordu. Verildiyse geçerliliği application katmanında doğrulanır.
    /// </summary>
    public Guid? NeighborhoodId { get; private set; }
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string? PostalCode { get; private set; }
    public Coordinate Coordinate { get; private set; }

    public static Result<Address> Create(
        Guid cityId,
        Guid districtId,
        Guid? neighborhoodId,
        string line1,
        Coordinate coordinate,
        string? line2 = null,
        string? postalCode = null)
    {
        if (string.IsNullOrWhiteSpace(line1))
            return Result.Failure<Address>(Error.Validation("Adres satırı gerekli."));
        if (line1.Length > 250)
            return Result.Failure<Address>(Error.Validation("Adres satırı çok uzun (en fazla 250 karakter)."));

        return new Address
        {
            CityId = cityId,
            DistrictId = districtId,
            NeighborhoodId = neighborhoodId,
            Line1 = line1.Trim(),
            Line2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim(),
            PostalCode = postalCode?.Trim(),
            Coordinate = coordinate,
        };
    }
}
