using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditLogReadTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record AuditLogItem(
        long Id, Guid? ActorUserId, string? ActorDisplayName, string ActorType,
        string ActionType, string ResourceType, string ResourceId,
        string? PayloadJson, string? Ip, string? RequestId, DateTimeOffset CreatedAt);
    public sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, int Page, int PageSize, int TotalCount);

    [Fact]
    public async Task Admin_audit_log_lists_recent_admin_actions_with_payload_and_actor_name()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-audit@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Audit Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        // perform an admin-only action that writes to audit_logs (shop suspend)
        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var suspend = await adminClient.PostAsJsonAsync(
            $"/v1/admin/barber-shops/{shop.Id}/suspend",
            new { reason = "Audit log testi — soruşturma süresince askıda." });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        // act: admin reads audit log filtered by action type
        var page = await adminClient.GetJson<AuditLogPage>(
            "/v1/admin/audit-logs?actionType=admin.barbershop.suspended&pageSize=10");
        Assert.True(page.TotalCount >= 1, "Suspend action audit log'a yazılmalı");
        var entry = page.Items.First(i => i.ResourceId == shop.Id.ToString());
        Assert.Equal("admin.barbershop.suspended", entry.ActionType);
        Assert.Equal("BarberShop", entry.ResourceType);
        Assert.NotNull(entry.ActorUserId);
        Assert.Equal("Audit Admin", entry.ActorDisplayName);
        Assert.NotNull(entry.PayloadJson);
        Assert.Contains("reason", entry.PayloadJson, StringComparison.OrdinalIgnoreCase);

        // filter by resource (shop) — minimum 1 entry
        var byResource = await adminClient.GetJson<AuditLogPage>(
            $"/v1/admin/audit-logs?resourceType=BarberShop&resourceId={shop.Id}&pageSize=10");
        Assert.True(byResource.TotalCount >= 1);
        Assert.All(byResource.Items, i => Assert.Equal(shop.Id.ToString(), i.ResourceId));
    }

    [Fact]
    public async Task Audit_log_endpoint_requires_admin_role()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var customer = factory.CreateClient();
        customer.SetBearer((await customer.DevLoginAsync("Customer")).AccessToken);

        var resp = await customer.GetAsync("/v1/admin/audit-logs");
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized,
            $"Customer admin endpoint'e erişememeli (got {(int)resp.StatusCode})");
    }
}
