using Hangfire;
using Hangfire.PostgreSql;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Infrastructure.Auth;
using Kuaforum.Infrastructure.BackgroundJobs;
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

        services.AddHttpContextAccessor();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IRequestContext, HttpContextRequestContext>();
        services.AddScoped<IAuditLogger, AuditLogger>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<GoogleOAuthOptions>(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.Configure<AppleOAuthOptions>(configuration.GetSection(AppleOAuthOptions.SectionName));

        services.AddHttpClient();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddScoped<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IOAuthProvider, GoogleOAuthProvider>();
        services.AddSingleton<IOAuthProvider, AppleOAuthProvider>();

        var smsProvider = configuration.GetValue<string>("Sms:Provider")?.ToLowerInvariant() ?? "mock";
        if (smsProvider == "mock")
        {
            services.AddSingleton<ISmsService, MockSmsService>();
        }
        else
        {
            services.AddSingleton<ISmsService, MockSmsService>();
        }

        services.AddHangfire(cfg =>
        {
            cfg.UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(pgConnString));
            cfg.UseSimpleAssemblyNameTypeSerializer();
            cfg.UseRecommendedSerializerSettings();
        });
        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = Environment.ProcessorCount;
            opts.Queues = ["default", "critical"];
        });
        services.AddScoped<HardDeleteUsersJob>();
        services.AddScoped<IBusyRangeQuery, BookingBusyRangeQuery>();

        return services;
    }

    public static void RegisterRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        manager.AddOrUpdate<HardDeleteUsersJob>(
            recurringJobId: HardDeleteUsersJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: HardDeleteUsersJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
