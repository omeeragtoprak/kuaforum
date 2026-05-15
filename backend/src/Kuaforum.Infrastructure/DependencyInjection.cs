using Hangfire;
using Hangfire.PostgreSql;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Availability;
using Kuaforum.Infrastructure.Auth;
using Kuaforum.Infrastructure.Storage;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Caching;
using Kuaforum.Infrastructure.Notifications;
using Kuaforum.Infrastructure.Payments;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<Persistence.Seeding.AdminSeederOptions>(configuration.GetSection(Persistence.Seeding.AdminSeederOptions.SectionName));

        services.AddHttpClient();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISymmetricEncryption, AesEncryptionService>();
        services.AddScoped<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IOAuthProvider, GoogleOAuthProvider>();
        services.AddSingleton<IOAuthProvider, AppleOAuthProvider>();

        services.Configure<PayTrOptions>(configuration.GetSection(PayTrOptions.SectionName));
        services.Configure<IyzicoOptions>(configuration.GetSection(IyzicoOptions.SectionName));
        services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
        services.AddSingleton<IPaymentProvider, PayTrPaymentProvider>();
        services.AddSingleton<IPaymentProvider, IyzicoPaymentProvider>();
        services.AddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();

        // Push channel: Firebase Cloud Messaging if credentials configured, else log-only mock.
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        var fcmOpts = configuration.GetSection(FcmOptions.SectionName).Get<FcmOptions>() ?? new FcmOptions();
        var hasFcmCreds = !string.IsNullOrWhiteSpace(fcmOpts.ServiceAccountJson)
            || (!string.IsNullOrWhiteSpace(fcmOpts.ServiceAccountJsonPath) && File.Exists(fcmOpts.ServiceAccountJsonPath));
        if (hasFcmCreds)
        {
            // Instantiate FirebaseApp eagerly via temp logger so it's available at DI time.
            using var bootstrapFactory = LoggerFactory.Create(b => b.AddConsole());
            var bootstrapLogger = bootstrapFactory.CreateLogger<FcmPushChannel>();
            var firebaseApp = FirebaseAppFactory.CreateOrNull(fcmOpts, bootstrapLogger);
            if (firebaseApp is not null)
            {
                services.AddSingleton(firebaseApp);
                services.AddScoped<INotificationChannelHandler, FcmPushChannel>();
            }
            else
            {
                services.AddScoped<INotificationChannelHandler, MockPushChannel>();
            }
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, MockPushChannel>();
        }
        services.AddScoped<INotificationChannelHandler, MockSmsChannel>();
        services.AddScoped<INotificationChannelHandler, InAppNotificationChannel>();

        // Email channel: Resend if API key configured, else fallback to log-only mock.
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        var emailProvider = configuration.GetValue<string>("Email:Provider")?.ToLowerInvariant() ?? "mock";
        if (emailProvider == "resend")
        {
            services.AddScoped<INotificationChannelHandler, ResendEmailChannel>();
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, MockEmailChannel>();
        }

        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // SMS: NetGSM if configured, else Mock for dev.
        services.Configure<NetGsmOptions>(configuration.GetSection(NetGsmOptions.SectionName));
        var smsProvider = configuration.GetValue<string>("Sms:Provider")?.ToLowerInvariant() ?? "mock";
        if (smsProvider == "netgsm")
        {
            services.AddSingleton<ISmsService, NetGsmSmsService>();
        }
        else
        {
            services.AddSingleton<ISmsService, MockSmsService>();
        }

        // Hangfire storage Postgres'e EAGER bağlanır (UsePostgreSqlStorage builder
        // sırasında schema check yapar). Test ortamında Testcontainers ayrı dynamic
        // port'ta açılırken Hangfire default 5432'ye bağlanıp connection refused.
        // Flag ile devre dışı bırakılabilir: appsettings/env'de "Hangfire:Enabled=false".
        var hangfireEnabled = configuration.GetValue<bool?>("Hangfire:Enabled") ?? true;
        if (hangfireEnabled)
        {
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
        }
        services.AddScoped<HardDeleteUsersJob>();
        services.AddScoped<ExpireWalkInJob>();
        services.AddScoped<AutoNoShowJob>();
        services.AddScoped<BookingReminderJob>();
        services.AddScoped<IBusyRangeQuery, BookingBusyRangeQuery>();
        services.AddScoped<IStylistScheduleChecker, StylistScheduleChecker>();
        services.AddSingleton<IObjectStorage, LocalFileObjectStorage>();
        services.AddScoped<GeographySeeder>();
        services.AddScoped<DemoBarberSeeder>();
        services.AddScoped<ComprehensiveDemoSeeder>();
        services.AddScoped<AdminSeeder>();

        return services;
    }

    public static async Task SeedReferenceDataAsync(
        IServiceProvider services,
        bool seedDemoBarbers,
        bool seedComprehensiveDemo,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        // Apply pending EF migrations before any seeder hits the schema. Idempotent —
        // already-applied migrations are no-ops. Required so fresh Testcontainers DBs
        // and brand-new prod boots don't fail on missing tables/columns.
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.MigrateAsync(ct);

        var geo = scope.ServiceProvider.GetRequiredService<GeographySeeder>();
        await geo.SeedAsync(ct);

        var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
        await adminSeeder.SeedAsync(ct);

        if (seedDemoBarbers)
        {
            var demo = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
            await demo.SeedAsync(ct);
        }

        if (seedComprehensiveDemo)
        {
            var comprehensive = scope.ServiceProvider.GetRequiredService<ComprehensiveDemoSeeder>();
            await comprehensive.SeedAsync(ct);
        }
    }

    public static void RegisterRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        manager.AddOrUpdate<HardDeleteUsersJob>(
            recurringJobId: HardDeleteUsersJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: HardDeleteUsersJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        manager.AddOrUpdate<ExpireWalkInJob>(
            recurringJobId: ExpireWalkInJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: ExpireWalkInJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        manager.AddOrUpdate<AutoNoShowJob>(
            recurringJobId: AutoNoShowJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: AutoNoShowJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        manager.AddOrUpdate<BookingReminderJob>(
            recurringJobId: BookingReminderJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: BookingReminderJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
