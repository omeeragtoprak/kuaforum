using Kuaforum.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Kuaforum.Tests.Integration;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("nickblah/postgis:16-postgis-3.4") // multi-arch (amd64 + arm64/v8)
        .WithDatabase("kuaforum_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Touch HttpClient to trigger Program start (which runs migrations + seeds).
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
                // Hangfire UsePostgreSqlStorage eager bağlanır; testte ayrı bir
                // Postgres instance açmaktan kaçınmak için devre dışı.
                ["Hangfire:Enabled"] = "false",
            });
        });

        // ConfigureAppConfiguration tek başına yeterli değil — AddInfrastructure
        // pgConnString'i ConfigurationManager build edilirken **eager** okuyor,
        // testteki InMemory override bazı durumlarda timing nedeniyle
        // appsettings.json fallback'ine düşüyor (CI'da gözlemlendi).
        // En kesin yöntem: ConfigureTestServices ile DbContextOptions'ı doğrudan
        // Testcontainers connection string'i ile yeniden register et.
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
