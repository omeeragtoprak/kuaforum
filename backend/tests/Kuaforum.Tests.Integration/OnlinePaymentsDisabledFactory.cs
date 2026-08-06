using Kuaforum.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-009 ücretsiz büyüme modu: <see cref="IntegrationTestFactory"/>'nin Development
/// environment'ı <c>appsettings.Development.json</c> üzerinden <c>Payment:OnlinePaymentsEnabled</c>
/// değerini <c>true</c>'ya override ediyor (RFC-006 kapora/webhook suite'i regresyonsuz çalışsın
/// diye) — bu yüzden flag=false davranışını (409 initiate guard + ExpirePendingBookingsJob'un
/// randevu-saatine-kadar eşiği) test etmek için ayrı bir factory gerekiyor. Aynı
/// <see cref="ObjectStorageFailFastTests"/>'in kendi throwaway Postgres+Redis'i açma deseni:
/// paylaşılan <see cref="IntegrationTestFactory"/> zaten farklı bir config ile boot olmuş,
/// config test-içinde değiştirilemez. Bu fixture <see cref="IntegrationTestCollection"/>'a ikinci
/// bir <c>ICollectionFixture</c> olarak eklenir (bkz. o dosyadaki not) — ayrı bir collection
/// olsaydı xUnit'in varsayılan davranışıyla paralel çalışırdı.
/// </summary>
public sealed class OnlinePaymentsDisabledFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static OnlinePaymentsDisabledFactory()
    {
        // Same static-ctor timing workaround IntegrationTestFactory uses for Hangfire.
        Environment.SetEnvironmentVariable("Hangfire__Enabled", "false");
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("nickblah/postgis:16-postgis-3.4")
        .WithDatabase("kuaforum_test_payments_disabled")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        using var client = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                ["Seed:Demo"] = "true",
                ["Seed:ComprehensiveDemo"] = "false",
                ["Hangfire:Enabled"] = "false",
                // The setting under test: base appsettings.json already defaults this to false,
                // but appsettings.Development.json (loaded because UseEnvironment("Development")
                // above) overrides it back to true — this in-memory provider is added AFTER both
                // JSON files in the config pipeline, so it wins and restores the fail-closed value.
                ["Payment:OnlinePaymentsEnabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor is not null) services.Remove(dbDescriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(_postgres.GetConnectionString(), npg =>
                {
                    npg.UseNetTopologySuite();
                    npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
                });
            });

            var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor is not null) services.Remove(redisDescriptor);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
              notification_logs,
              audit_logs,
              shop_subscriptions,
              barber_replies, reviews,
              refunds, payments,
              booking_items, bookings,
              stylist_services, time_offs, working_hours, stylists, service_catalogs,
              kyc_documents, kyc_submissions, barber_shops,
              consents, refresh_tokens, otp_codes, admin_credentials, users
            RESTART IDENTITY CASCADE;
        """);
    }
}
