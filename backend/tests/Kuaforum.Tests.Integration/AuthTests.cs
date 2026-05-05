using System.Net;
using System.Net.Http.Json;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AuthTests(IntegrationTestFactory factory)
{
    [Fact]
    public async Task DevLogin_Customer_returns_token_and_user()
    {
        using var client = factory.CreateClient();

        var login = await client.DevLoginAsync("Customer");

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        Assert.NotEqual(Guid.Empty, login.UserId);
        Assert.Equal("Customer", login.Role);
    }

    [Fact]
    public async Task DevLogin_BarberOwner_returns_demo_seeded_owner()
    {
        using var client = factory.CreateClient();

        var login = await client.DevLoginAsync("BarberOwner");

        Assert.Equal("BarberOwner", login.Role);
        Assert.NotEqual(Guid.Empty, login.UserId);
    }

    [Fact]
    public async Task GetMe_with_token_returns_profile()
    {
        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var resp = await client.GetAsync("/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetMe_without_token_returns_401()
    {
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

}
