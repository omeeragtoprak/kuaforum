using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Infrastructure.Caching;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Koyeb free-tier deploy hazırlığı (2026-08): Redis'siz bir dağıtımın gerçekten ayağa
/// kalktığını ve randevu slot çakışma korumasının (asıl garanti PostgreSQL EXCLUDE constraint'i
/// — M15_BookingSlotExclusion) Redis olmadan da 409 döndürdüğünü kanıtlar.
///
/// <see cref="IntegrationTestFactory"/>'nin aksine BİLEREK Redis Testcontainer'ı YOK.
/// <c>ConnectionStrings:Redis</c>'i config'ten SİLMEYE çalışmak (<c>ConfigureAppConfiguration</c>
/// ile) YETERSİZ kaldı — <c>AddInfrastructure</c> bunu Build() sırasında EAGER okuyor ve
/// <see cref="IntegrationTestFactory"/>'nin kendi yorumunda belgelediği timing sorunuyla aynı
/// nedenle appsettings.Development.json'daki <c>"localhost:6380"</c> default'una düşüp (varsa)
/// GERÇEK bir yerel Redis'e bağlanabiliyordu (bu factory'nin ilk sürümünde ölçülen, yanlış-pozitif
/// bir "ok" health check ile yakalandı). Bu yüzden aynı dosyanın Postgres/Redis için kullandığı
/// deseni izliyoruz: config değil, <c>ConfigureTestServices</c> içinde DI descriptor'larını
/// doğrudan söküp <see cref="InMemoryRateLimiter"/>/<see cref="InMemoryDistributedLock"/> ile
/// deterministik olarak değiştiriyoruz — "Redis hiç yokmuş gibi" bir DI şekli garanti eder.
/// AddInfrastructure'ın config-tabanlı dal seçiminin GERÇEK bir process boot'unda (test host'un
/// competing ConfigureAppConfiguration yarışı olmadan) doğru çalıştığı ayrıca elle doğrulandı —
/// bkz. docs/DEPLOY-FREE-TIER.md "Doğrulama" bölümü.
/// </summary>
public sealed class NoRedisIntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static NoRedisIntegrationTestFactory()
    {
        Environment.SetEnvironmentVariable("Hangfire__Enabled", "false");
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("nickblah/postgis:16-postgis-3.4")
        .WithDatabase("kuaforum_test_no_redis")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var client = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
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
                // appsettings.Development.json'daki "ConnectionStrings:Redis": "localhost:6380"
                // default'unu boşa çekmeyi DENER (aynı sırada eklenen bu InMemoryCollection JSON
                // dosyalarından sonra geldiği için üzerine yazmalı) — ama AddInfrastructure bunu
                // Build() sırasında EAGER okuduğundan bu tek başına GÜVENİLİR değil (bkz. sınıf
                // XML doc'u). Asıl garanti aşağıdaki ConfigureTestServices'teki descriptor sökümü.
                ["ConnectionStrings:Redis"] = "",
                ["Seed:Demo"] = "true",
                ["Seed:ComprehensiveDemo"] = "false",
                ["Seed:RichDemo"] = "false",
                ["Hangfire:Enabled"] = "false",
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

            // Redis-backed kayıtları DETERMİNİSTİK olarak sök ve in-memory karşılıklarla
            // değiştir — config timing'e güvenmek yerine (bkz. sınıf XML doc'u, bu factory'nin
            // ilk sürümü tam da bu yüzden gerçek bir yerel Redis'e sessizce bağlanmıştı).
            var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor is not null) services.Remove(redisDescriptor);

            var rateLimiterDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRateLimiter));
            if (rateLimiterDescriptor is not null) services.Remove(rateLimiterDescriptor);
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();

            var lockDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDistributedLock));
            if (lockDescriptor is not null) services.Remove(lockDescriptor);
            services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
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
              subscription_invoice_payments, subscription_invoices,
              shop_subscriptions,
              barber_replies, reviews,
              refunds, payments,
              booking_complaints, booking_items, bookings,
              stylist_services, time_offs, working_hours, stylists, service_catalogs,
              kyc_documents, kyc_submissions, barber_shops,
              consents, refresh_tokens, otp_codes, admin_credentials, users
            RESTART IDENTITY CASCADE;
        """);
    }
}
