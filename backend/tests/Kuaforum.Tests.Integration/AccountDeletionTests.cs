using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AccountDeletionTests(IntegrationTestFactory factory)
{
    public sealed record DeleteResp(string Message);
    public sealed record SessionDto(Guid Id, string DeviceFingerprint, string? DeviceName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, string? LastUsedIp, bool IsCurrent);

    [Fact]
    public async Task Delete_then_restore_lets_user_login_again()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var c1 = factory.CreateClient();
        var login1 = await c1.DevLoginAsync("Customer");
        c1.SetBearer(login1.AccessToken);
        var userId = login1.UserId;

        // act: customer deletes account
        var del = await c1.DeleteAsync("/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        var delBody = await del.Content.ReadFromJsonAsync<DeleteResp>(TestHelpers.Json);
        Assert.NotNull(delBody);
        Assert.Contains("30 days", delBody!.Message, StringComparison.OrdinalIgnoreCase);

        // assert: DB state — DeletedAt set, refresh tokens revoked
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var user = await db.Set<Kuaforum.Domain.Modules.Identity.User>().FirstAsync(u => u.Id == userId);
            Assert.NotNull(user.DeletedAt);
            Assert.Equal(Kuaforum.Domain.Modules.Identity.UserStatus.Deleted, user.Status);
            Assert.False(user.CanLogin);

            var activeTokens = await db.Set<Kuaforum.Domain.Modules.Identity.RefreshToken>()
                .CountAsync(t => t.UserId == userId && t.RevokedAt == null);
            Assert.Equal(0, activeTokens);
        }

        // act 2: tekrar dev-login → CanLogin=false → 403/400
        using var c2 = factory.CreateClient();
        var reLoginResp = await c2.PostAsJsonAsync("/v1/auth/dev-login", new { role = "Customer" });
        Assert.False(reLoginResp.IsSuccessStatusCode,
            $"Soft-deleted user dev-login etmemeli (got {(int)reLoginResp.StatusCode})");

        // act 3: restore via existing JWT (still valid for 15 minutes)
        var restore = await c1.PostAsync("/v1/users/me/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        // act 4: yeni dev-login artık çalışmalı (CanLogin=true)
        using var c3 = factory.CreateClient();
        var reLoginOk = await c3.DevLoginAsync("Customer");
        Assert.Equal(userId, reLoginOk.UserId);
    }

    [Fact]
    public async Task Delete_twice_returns_error()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var c = factory.CreateClient();
        c.SetBearer((await c.DevLoginAsync("Customer")).AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync("/v1/users/me")).StatusCode);
        var second = await c.DeleteAsync("/v1/users/me");
        Assert.False(second.IsSuccessStatusCode,
            $"İkinci silme reddedilmeli (got {(int)second.StatusCode})");
    }

    [Fact]
    public async Task Revoking_other_session_keeps_current_session_alive()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var deviceA = factory.CreateClient();
        deviceA.DefaultRequestHeaders.Add("X-Device-Fingerprint", "device-a-fingerprint");
        var loginA = await deviceA.DevLoginAsync("Customer");
        deviceA.SetBearer(loginA.AccessToken);

        using var deviceB = factory.CreateClient();
        deviceB.DefaultRequestHeaders.Add("X-Device-Fingerprint", "device-b-fingerprint");
        var loginB = await deviceB.DevLoginAsync("Customer");
        deviceB.SetBearer(loginB.AccessToken);

        // both clients see >=1 session each
        var sessionsA = await deviceA.GetJson<IReadOnlyList<SessionDto>>("/v1/users/me/sessions");
        Assert.True(sessionsA.Count >= 2, $"A görmeli: en az 2 oturum, bulunan: {sessionsA.Count}");
        var sessionOnB = sessionsA.FirstOrDefault(s => !s.IsCurrent);
        if (sessionOnB is null)
        {
            // single-row case: list ordering means we may need to fetch from B
            var sessionsB = await deviceB.GetJson<IReadOnlyList<SessionDto>>("/v1/users/me/sessions");
            sessionOnB = sessionsB.First(s => s.IsCurrent);
        }
        Assert.NotNull(sessionOnB);

        // revoke that "other" session via A
        var revoke = await deviceA.DeleteAsync($"/v1/users/me/sessions/{sessionOnB!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // A's profile call should still succeed (its session intact)
        var profile = await deviceA.GetAsync("/v1/users/me");
        Assert.True(profile.IsSuccessStatusCode);
    }
}
