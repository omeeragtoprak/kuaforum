using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Geography;

public sealed class District : Entity
{
    private District() { }

    public Guid CityId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    public static District Create(Guid cityId, string name)
    {
        return new District
        {
            CityId = cityId,
            Name = name,
            Slug = SlugHelper.Make(name),
        };
    }
}
