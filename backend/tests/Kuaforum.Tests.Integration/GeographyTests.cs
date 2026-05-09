using System.Net;
using System.Net.Http.Json;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class GeographyTests(IntegrationTestFactory factory)
{
    public sealed record CityDto(Guid Id, int PlateCode, string Name, string Slug, int ActiveShopCount);

    public sealed record SearchItemDto(Guid Id, string Name, string Slug);
    public sealed record SearchRespDto(IReadOnlyList<SearchItemDto> Items, int Page, int PageSize, int TotalCount);

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
        Assert.Contains(cities, c => c.PlateCode == 25 && c.Name == "Erzurum");
    }

    [Fact]
    public async Task Erzurum_default_city_has_at_least_six_active_demo_shops()
    {
        // Demo customer'ın varsayılan şehri — keşfet sayfası bu seed'e güvenir.
        using var client = factory.CreateClient();
        var cities = await client.GetJson<IReadOnlyList<CityDto>>("/v1/geography/cities");
        var erzurum = cities.Single(c => c.PlateCode == 25);

        var search = await client.GetJson<SearchRespDto>($"/v1/barber-shops/search?cityId={erzurum.Id}&pageSize=20");
        Assert.True(search.TotalCount >= 6,
            $"Erzurum'da en az 6 demo kuaför bekleniyor (DemoBarberSeeder), bulunan: {search.TotalCount}");
        Assert.Contains(search.Items, s => s.Name.Contains("Erzurum", StringComparison.OrdinalIgnoreCase)
            || s.Slug.Contains("erzurum", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Palandöken", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Aziziye", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Çifte Minare", StringComparison.OrdinalIgnoreCase));
    }
}
