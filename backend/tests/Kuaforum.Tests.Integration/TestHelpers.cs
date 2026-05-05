using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kuaforum.Tests.Integration;

internal static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record AuthenticatedUserDto(Guid Id, string? Phone, string? DisplayName, string Role);
    public sealed record DevLoginResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessExpiresAt,
        DateTimeOffset RefreshExpiresAt,
        AuthenticatedUserDto User)
    {
        public Guid UserId => User.Id;
        public string Role => User.Role;
    }

    public static async Task<DevLoginResponse> DevLoginAsync(this HttpClient client, string role)
    {
        var resp = await client.PostAsJsonAsync("/v1/auth/dev-login", new { role });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<DevLoginResponse>(Json);
        return body!;
    }

    public static void SetBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<T> GetJson<T>(this HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body)
    {
        return await client.PostAsJsonAsync(url, body, Json);
    }
}
