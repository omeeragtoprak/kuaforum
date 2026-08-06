using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Regresyon kilidi (canlı Development sunucusunda, 2026-08-06 tarihinde gerçekten
/// yakalanan bug — RichDemoSeeder/dev-up.sh doğrulaması sırasında): iki endpoint
/// <c>DateTimeOffset.UtcNow.Date</c> kalıbını kullanıyordu. Bu ifade bir
/// <c>DateTimeOffset</c> DEĞİL, bir <c>DateTime</c> döner; implicit dönüşüm sunucunun
/// YEREL saat dilimini kullanır. Sunucu OS'i UTC olmayan bir saat diliminde çalışıyorsa
/// (bu makinede Europe/Istanbul, +03:00) Npgsql "timestamp with time zone yalnız Offset=0
/// kabul eder" hatasıyla patlar.
///
/// Testcontainers/CI konteynerleri genelde UTC olduğundan bu spesifik hata modu burada
/// YENİDEN ÜRETİLEMEZ — ama mevcut testlerin İKİSİ de (BookingFlowTests, DepositPaymentTests)
/// yalnızca EXPLICIT from/to query param'larıyla çağırıyordu; hatalı varsayılan-değer dalı
/// (`from`/`to` OMITTED — mobile/gerçek kullanıcı akışının varsayılan yolu) hiç test
/// edilmemişti. Bu testler o boşluğu kapatır.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TimezoneDefaultQueryTests(IntegrationTestFactory factory)
{
    [Fact]
    public async Task GetOwnerBookings_WithoutFromToQueryParams_UsesUtcDefaultRange_Returns200()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var resp = await ownerClient.GetAsync("/v1/owner/barber-shop/bookings"); // from/to bilerek verilmiyor
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminDashboard_UsesUtcTodayBoundary_Returns200()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-tzdefault@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Test Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync();
        }

        using var adminClient = factory.CreateClient();
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var resp = await adminClient.GetAsync("/v1/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
