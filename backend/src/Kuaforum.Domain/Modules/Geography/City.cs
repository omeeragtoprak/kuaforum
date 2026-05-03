using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Geography;

public sealed class City : Entity
{
    private City() { }

    public string Name { get; private set; } = null!;
    public int PlateCode { get; private set; }
    public string Slug { get; private set; } = null!;

    public static City Create(int plateCode, string name)
    {
        return new City
        {
            Name = name,
            PlateCode = plateCode,
            Slug = SlugHelper.Make(name),
        };
    }
}
