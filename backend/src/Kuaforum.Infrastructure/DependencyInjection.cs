using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Infrastructure.Auth;
using Kuaforum.Infrastructure.Caching;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Kuaforum.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var pgConnString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(pgConnString, npg =>
            {
                npg.UseNetTopologySuite();
                npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var redisConnString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is missing.");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnString));

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddScoped<IRateLimiter, RedisRateLimiter>();

        var smsProvider = configuration.GetValue<string>("Sms:Provider")?.ToLowerInvariant() ?? "mock";
        if (smsProvider == "mock")
        {
            services.AddSingleton<ISmsService, MockSmsService>();
        }
        else
        {
            // NetGSMSmsService will be added when real provider is wired (Phase 1 mid).
            services.AddSingleton<ISmsService, MockSmsService>();
        }

        return services;
    }
}
