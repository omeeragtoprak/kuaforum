using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Geography;

public sealed class Neighborhood : Entity
{
    private Neighborhood() { }

    public Guid DistrictId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? PostalCode { get; private set; }

    public static Neighborhood Create(Guid districtId, string name, string? postalCode = null)
    {
        return new Neighborhood
        {
            DistrictId = districtId,
            Name = name,
            Slug = SlugHelper.Make(name),
            PostalCode = postalCode,
        };
    }
}
