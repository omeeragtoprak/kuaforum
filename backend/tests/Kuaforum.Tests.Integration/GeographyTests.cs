using System.Net;
using System.Net.Http.Json;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class GeographyTests(IntegrationTestFactory factory)
{
    public sealed record CityDto(Guid Id, int PlateCode, string Name, string Slug);

    [Fact]
    public async Task Cities_returns_81_seeded_il()
    {
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/v1/geography/cities");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var cities = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CityDto>>(TestHelpers.Json);
        Assert.NotNull(cities);
        Assert.Equal(81, cities!.Count);
        Assert.Contains(cities, c => c.Name.StartsWith("İstanbul", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cities, c => c.PlateCode == 6 && c.Name == "Ankara");
    }
}
