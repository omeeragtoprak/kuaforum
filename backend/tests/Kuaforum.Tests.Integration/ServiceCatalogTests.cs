using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ServiceCatalogTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor, bool IsActive);
    public sealed record CreateResp(Guid Id);

    [Fact]
    public async Task Owner_can_create_update_and_soft_delete_service()
    {
        await factory.ResetDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        // create
        var createResp = await ownerClient.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "Test Saç Kesim",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = 30000L,
            bufferMinutes = 5,
            description = "İlk açıklama",
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<CreateResp>(TestHelpers.Json);
        Assert.NotNull(created);

        // update (price + duration + name)
        var updateResp = await ownerClient.PutAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services/{created!.Id}", new
        {
            name = "Test Saç Kesim PRO",
            durationMinutes = 45,
            priceMinor = 50000L,
            bufferMinutes = 10,
            description = "Güncellendi",
        });
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        // verify update reflected in shop services list
        var afterUpdate = await ownerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");
        var updated = afterUpdate.Single(s => s.Id == created.Id);
        Assert.Equal("Test Saç Kesim PRO", updated.Name);
        Assert.Equal(45, updated.DurationMinutes);
        Assert.Equal(50000, updated.PriceMinor);
        Assert.Equal(10, updated.BufferMinutes);
        Assert.True(updated.IsActive);

        // delete (soft) — service should disappear from list
        var deleteResp = await ownerClient.DeleteAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var afterDelete = await ownerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");
        Assert.DoesNotContain(afterDelete, s => s.Id == created.Id);
    }

    [Fact]
    public async Task Other_owner_cannot_update_or_delete_someone_elses_service()
    {
        await factory.ResetDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerA = factory.CreateClient();
        ownerA.SetBearer((await ownerA.DevLoginAsync("BarberOwner")).AccessToken);
        var shopA = await ownerA.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        // owner A creates a service
        var createResp = await ownerA.PostAsJsonAsync($"/v1/owner/barber-shop/{shopA.Id}/services", new
        {
            name = "Korunan Hizmet",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = 25000L,
            bufferMinutes = 0,
            description = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<CreateResp>(TestHelpers.Json);

        // attacker (customer role, no shop) tries to PUT/DELETE — should be 4xx
        using var attacker = factory.CreateClient();
        attacker.SetBearer((await attacker.DevLoginAsync("Customer")).AccessToken);

        var maliciousUpdate = await attacker.PutAsJsonAsync($"/v1/owner/barber-shop/{shopA.Id}/services/{created!.Id}", new
        {
            name = "Hacked",
            priceMinor = 1L,
        });
        Assert.True(maliciousUpdate.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);

        var maliciousDelete = await attacker.DeleteAsync($"/v1/owner/barber-shop/{shopA.Id}/services/{created.Id}");
        Assert.True(maliciousDelete.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);

        // confirm service still intact
        var stillThere = await ownerA.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shopA.Id}/services");
        Assert.Contains(stillThere, s => s.Id == created.Id && s.Name == "Korunan Hizmet");
    }
}
