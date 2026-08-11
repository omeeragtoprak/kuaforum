using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Availability;
using Kuaforum.Application.Modules.Payment;
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
using Microsoft.Extensions.Options;
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

        // Redis OPSİYONEL (2026-08 — Koyeb free-tier deploy hazırlığı, kredi kartsız/serverless
        // ücretsiz hosting hedefi Redis içermiyor). Boş/yok ise IConnectionMultiplexer HİÇ
        // register edilmez — bunu tüketen IRateLimiter/IDistributedLock in-memory karşılıklarına
        // düşer (bkz. InMemoryRateLimiter/InMemoryDistributedLock XML doc'ları: asıl garanti
        // zaten PostgreSQL EXCLUDE constraint'i — M15 — olduğundan tek-örnekli dağıtımda bu
        // güvenli bir davranış). Redis varsa davranış öncekiyle birebir aynı. Program.cs
        // /health/ready da IConnectionMultiplexer'ı IServiceProvider üzerinden OPSİYONEL
        // çözer — burada register edilmemesi orayı bozmaz.
        var redisConnString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnString));
            services.AddScoped<IRateLimiter, RedisRateLimiter>();
            services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        }
        else
        {
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
            services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
            services.AddHostedService<RedisUnavailableStartupNotice>();
        }

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

        // RFC-013 M3 (adversarial review): eski DepositRate >= CommissionRate invariantı
        // ANLAMSIZLAŞTI ve yanlış-pozitif boot failure üretiyordu — kaldırıldı. RFC-012 ile
        // komisyon artık ödeme anında hiç kesilmiyor (InitiatePaymentCommand'da
        // platformCommissionMinor sabit 0), RFC-013 C3 ile kapora oranı da global DepositRate
        // DEĞİL, per-shop shop.BookingPolicy.DepositPercent (bkz. PaymentOptions.DepositRate
        // XML doc — artık kullanılmayan, geriye dönük alan).
        //
        // Review fix (2026-08-07): Payment:CommissionRate config anahtarı + burada ValidateOnStart
        // ile ZORUNLU kılınması TAMAMEN kaldırıldı. RFC-014 §2 D1 ile gelir modeli sabit aylık
        // aboneliğe (SubscriptionInvoice) geçtiğinden bu oran hiçbir faturayı beslemiyordu; tek
        // etkisi InitiateBookingCommand üzerinden her booking'e (booking.PlatformCommissionMinor)
        // sıfırdan farklı bir tutar yazmaktı — para yoluna hiç girmeyen ama DB'de "platform
        // geliri" gibi görünen bir kolon (ileride rapor yazan biri için tuzak). Kolonu migration'la
        // kaldırmak bu paketin kapsamı dışında (Booking aggregate'i + pin testleri ayrı bir iş) —
        // bunun yerine InitiateBookingCommand artık sabit 0m literal geçiyor (bkz. orada), yani
        // yeni bookinglerde bu kolon hep 0. Config anahtarı appsettings.json'dan da kaldırıldı.
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName));

        services.AddHttpClient();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISymmetricEncryption, AesEncryptionService>();
        // IRateLimiter / IDistributedLock: Redis varsa/yoksa yukarıda (redisConnString kontrolü)
        // koşullu register edildi — burada tekrar edilmiyor.
        services.AddSingleton<IOAuthProvider, GoogleOAuthProvider>();
        services.AddSingleton<IOAuthProvider, AppleOAuthProvider>();

        services.Configure<PayTrOptions>(configuration.GetSection(PayTrOptions.SectionName));
        services.Configure<IyzicoOptions>(configuration.GetSection(IyzicoOptions.SectionName));
        services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
        services.AddSingleton<IPaymentProvider, PayTrPaymentProvider>();
        services.AddSingleton<IPaymentProvider, IyzicoPaymentProvider>();
        services.AddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();
        services.AddScoped<IBookingRefundService, BookingRefundService>();

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
            // Hangfire:QueuePollIntervalSeconds (opsiyonel, VARSAYILAN Hangfire.PostgreSql'in
            // kendi default'u — 15s, DEĞİŞTİRİLMEDİ, Hetzner'deki her zaman-açık Postgres için
            // bu gayet makul). Koyeb free-tier + Neon serverless deploy hazırlığı (2026-08):
            // Neon free tier ayda yalnız 100 CU-hour compute veriyor ve Hangfire'ın sürekli DB
            // polling'i (varsayılan 15s'de bir) Neon compute'unu HİÇ scale-to-zero'ya
            // düşürtmeyip bu bütçeyi ~16-17 günde tüketebilir [unverified 2026-08-11 — Neon'un
            // "hangi sorgu aktivite sayılır" davranışı ölçülmedi, ama polling sorgusu da bir
            // sorgudur]. Bu yüzden bu tek değer env'den override edilebilir hale getirildi —
            // Koyeb/Neon dağıtımında founder'ın örn. 120s'ye çekmesi için (bkz.
            // docs/DEPLOY-FREE-TIER.md).
            var queuePollIntervalSeconds = configuration.GetValue<int?>("Hangfire:QueuePollIntervalSeconds");
            var storageOptions = new PostgreSqlStorageOptions();
            if (queuePollIntervalSeconds is > 0)
                storageOptions.QueuePollInterval = TimeSpan.FromSeconds(queuePollIntervalSeconds.Value);

            services.AddHangfire(cfg =>
            {
                cfg.UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(pgConnString), storageOptions);
                cfg.UseSimpleAssemblyNameTypeSerializer();
                cfg.UseRecommendedSerializerSettings();
            });
            services.AddHangfireServer(opts =>
            {
                opts.WorkerCount = Environment.ProcessorCount;
                opts.Queues = ["default", "critical"];
            });
            services.AddScoped<IBookingRefundJobEnqueuer, HangfireBookingRefundJobEnqueuer>();
        }
        else
        {
            // Hangfire disabled (tests) → IBackgroundJobClient isn't registered. Fall back to
            // a no-op logger so DI still resolves — see NullBookingRefundJobEnqueuer.
            services.AddScoped<IBookingRefundJobEnqueuer, NullBookingRefundJobEnqueuer>();
        }
        services.AddScoped<HardDeleteUsersJob>();
        services.AddScoped<ExpireWalkInJob>();
        services.AddScoped<AutoNoShowJob>();
        services.AddScoped<BookingReminderJob>();
        services.AddScoped<ExpirePendingBookingsJob>();
        services.AddScoped<ProcessBookingRefundJob>();
        services.AddScoped<CloseMonthlySubscriptionInvoicesJob>();
        services.AddScoped<SuspendOverdueSubscriptionsJob>();
        services.AddScoped<IBusyRangeQuery, BookingBusyRangeQuery>();
        services.AddScoped<IStylistScheduleChecker, StylistScheduleChecker>();

        // Object storage: Local (dev/CI disk, default — prod boot stays safe until the
        // founder provisions an R2 bucket) or S3 (Cloudflare R2 in prod / MinIO in tests).
        // Fail-fast mirrors the PaymentOptions pattern above: only enforced when S3 is
        // actually selected, so flipping the switch without keys crashes at startup
        // instead of failing confusingly on the first KYC upload. GAP P1-6.
        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName))
            .Validate(
                o => !string.Equals(o.Provider, "S3", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(o.S3.ServiceUrl)
                        && !string.IsNullOrWhiteSpace(o.S3.Bucket)
                        && !string.IsNullOrWhiteSpace(o.S3.AccessKeyId)
                        && !string.IsNullOrWhiteSpace(o.S3.SecretAccessKey)),
                "ObjectStorage:Provider=S3 seçiliyken ObjectStorage:S3:ServiceUrl/Bucket/AccessKeyId/SecretAccessKey zorunlu " +
                    "(env: ObjectStorage__S3__ServiceUrl vb.). Bkz infra/README.md 'ObjectStorage'.")
            .ValidateOnStart();

        var objectStorageProvider = configuration.GetValue<string>($"{ObjectStorageOptions.SectionName}:Provider") ?? "Local";
        if (string.Equals(objectStorageProvider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var s3Opts = sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value.S3;
                var config = new AmazonS3Config
                {
                    ServiceURL = s3Opts.ServiceUrl,
                    ForcePathStyle = true, // MinIO/R2 need path-style addressing, not virtual-hosted-style
                    AuthenticationRegion = s3Opts.Region,
                };
                return new AmazonS3Client(s3Opts.AccessKeyId, s3Opts.SecretAccessKey, config);
            });
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalFileObjectStorage>();
        }

        services.AddScoped<GeographySeeder>();
        services.AddScoped<DemoBarberSeeder>();
        services.AddScoped<ComprehensiveDemoSeeder>();
        services.AddScoped<RichDemoSeeder>();
        services.AddScoped<DemoDataResetter>();
        services.AddScoped<AdminSeeder>();
        services.AddScoped<Persistence.Seeding.PlatformSettingsSeeder>();
        services.AddScoped<Persistence.Seeding.SubscriptionPlanSeeder>();
        services.AddScoped<IEntitlementService, Modules.Billing.EntitlementService>();

        return services;
    }

    public static async Task SeedReferenceDataAsync(
        IServiceProvider services,
        bool seedDemoBarbers,
        bool seedComprehensiveDemo,
        bool seedRichDemo = false,
        bool isProduction = false,
        bool forceReset = false,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        // Apply pending EF migrations before any seeder hits the schema. Idempotent —
        // already-applied migrations are no-ops. Required so fresh Testcontainers DBs
        // and brand-new prod boots don't fail on missing tables/columns.
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.MigrateAsync(ct);

        // Demo veri bayatlama koruması (2026-08-07 canlı bulgu — bkz. DemoDataResetter XML
        // doc): RichDemoSeeder tek-seferlik idempotent bir gate kullanır, randevu tarihleri
        // MUTLAK olduğu için "bugünün" randevusu bir gece sonra artık bugüne ait değildir.
        // forceReset=true (dev-only /v1/dev/reseed-demo endpoint'i) VEYA otomatik bayatlama
        // tespiti (IsStaleAsync — her boot'ta ücretsiz, iki basit SELECT) tetiklenirse,
        // coğrafya/plan tanımları HARİÇ tüm demo veri TRUNCATE edilip aşağıdaki seeder
        // zinciri sıfırdan (bu kez "şimdi"ye göre) çalışır. isProduction=true iken bu blok
        // TAMAMEN atlanır — DemoDataResetter kendi IHostEnvironment kontrolüyle de ayrıca
        // throw eder (çift katman, bkz. o sınıfın XML doc'u).
        if (seedRichDemo && !isProduction)
        {
            var resetter = scope.ServiceProvider.GetRequiredService<DemoDataResetter>();
            if (forceReset || await resetter.IsStaleAsync(ct))
                await resetter.ResetAsync(ct);
        }

        var geo = scope.ServiceProvider.GetRequiredService<GeographySeeder>();
        await geo.SeedAsync(ct);

        var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
        await adminSeeder.SeedAsync(ct);

        var platformSettingsSeeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.PlatformSettingsSeeder>();
        await platformSettingsSeeder.SeedAsync(seedDemoBankInfo: seedDemoBarbers || seedComprehensiveDemo || seedRichDemo, ct);

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

        // RFC follow-up (2026-08-06): founder "dummy veri bol olsun" isteği — Erzurum
        // ağırlıklı 13 salon + dev quick-login "Customer" personası için her randevu
        // durumu + yorum havuzu + faturalama örnekleri. RichDemoSeeder kendi içinde
        // DemoBarberSeeder'ı (idempotent) tekrar çağırır, bu yüzden seedDemoBarbers=false
        // iken bile güvenle çalışır — yine de flag'ler ayrı tutulur (explicit opt-in).
        if (seedRichDemo)
        {
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(ct);
        }

        // RFC-014 §2 D1: demo/comprehensive/rich seeder'lardan SONRA çalışmalı — onlar
        // BarberShop.Create()'i CreateBarberShopCommandHandler'ı (otomatik LAUNCH_FREE
        // aboneliği yaratan) atlayarak doğrudan çağırır. Backfill adımı bu yüzden en son.
        var subscriptionPlanSeeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.SubscriptionPlanSeeder>();
        await subscriptionPlanSeeder.SeedAsync(ct);
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

        manager.AddOrUpdate<ExpirePendingBookingsJob>(
            recurringJobId: ExpirePendingBookingsJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: ExpirePendingBookingsJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        manager.AddOrUpdate<CloseMonthlySubscriptionInvoicesJob>(
            recurringJobId: CloseMonthlySubscriptionInvoicesJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: CloseMonthlySubscriptionInvoicesJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        manager.AddOrUpdate<SuspendOverdueSubscriptionsJob>(
            recurringJobId: SuspendOverdueSubscriptionsJob.JobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: SuspendOverdueSubscriptionsJob.CronExpression,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
