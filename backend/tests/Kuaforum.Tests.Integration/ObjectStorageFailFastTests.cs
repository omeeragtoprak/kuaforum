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
/// Proves the ObjectStorage:Provider=S3 fail-fast guard (GAP P1-6 — mirrors the
/// PaymentOptions.ValidateOnStart pattern already used in DependencyInjection.AddInfrastructure)
/// actually fires through the real ASP.NET Core host startup pipeline, not just as an
/// isolated <c>Options.Validate()</c> unit check. Deliberately does NOT spin up MinIO/R2:
/// the whole point of fail-fast is that boot must throw BEFORE any S3 client is ever
/// constructed or touched, so no S3-compatible server needs to exist for this test to be
/// meaningful. Runs its own throwaway Postgres+Redis (not the shared
/// <see cref="IntegrationTestFactory"/> collection) since it needs config unavailable to
/// that already-booted shared instance.
/// </summary>
public sealed class ObjectStorageFailFastTests
{
    [Fact]
    public async Task Boot_fails_fast_when_provider_is_S3_and_keys_are_missing()
    {
        await using var postgres = new PostgreSqlBuilder("nickblah/postgis:16-postgis-3.4")
            .WithDatabase("kuaforum_objectstorage_failfast")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await using var redis = new RedisBuilder("redis:7-alpine").Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        // Same static-ctor timing workaround IntegrationTestFactory uses for Hangfire.
        Environment.SetEnvironmentVariable("Hangfire__Enabled", "false");

        await using var factory = new MisconfiguredS3Factory(postgres.GetConnectionString(), redis.GetConnectionString());

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("ObjectStorage:Provider=S3", ex!.ToString());
    }

    private sealed class MisconfiguredS3Factory(string postgresConnStr, string redisConnStr) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = postgresConnStr,
                    ["ConnectionStrings:Redis"] = redisConnStr,
                    ["Seed:Demo"] = "false",
                    ["Seed:ComprehensiveDemo"] = "false",
                    ["Hangfire:Enabled"] = "false",
                    // The misconfiguration under test: S3 selected, no ObjectStorage:S3:* keys.
                    ["ObjectStorage:Provider"] = "S3",
                });
            });

            // Same ConfigureTestServices override IntegrationTestFactory uses — config-timing
            // during AddInfrastructure isn't reliable enough on its own in CI.
            builder.ConfigureTestServices(services =>
            {
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (dbDescriptor is not null) services.Remove(dbDescriptor);
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseNpgsql(postgresConnStr, npg =>
                    {
                        npg.UseNetTopologySuite();
                        npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
                    });
                });

                var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (redisDescriptor is not null) services.Remove(redisDescriptor);
                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnStr));
            });
        }
    }
}
