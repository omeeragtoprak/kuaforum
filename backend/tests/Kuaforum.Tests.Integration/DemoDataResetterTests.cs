using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// DemoDataResetter (2026-08-07 canlı bulgu fix): RichDemoSeeder tek-seferlik idempotent gate
/// kullandığı ve randevu tarihleri MUTLAK olduğu için, seed'lenmiş demo veri bir gece sonra
/// "bugüne ait randevu yok" hale gelip owner/admin panellerini boş bırakıyordu. Bu testler:
/// (1) TRUNCATE'in Production'da ASLA çalışmadığını, (2) bayatlama tespitinin (IsStaleAsync)
/// doğru çalıştığını, (3) forceReset ile art arda iki tazelemenin çift kayıt ÜRETMEDİĞİNİ,
/// (4) tazeleme sonrası dev-login "BarberOwner" personasının salonunda BUGÜNE ait randevu
/// olduğunu doğrular.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DemoDataResetterTests(IntegrationTestFactory factory)
{
    private const string RichGatePhone = "+905002000001"; // RichDemoSeeder.RichShopOwnerPhones[0]
    private const string DevLoginOwnerPhone = "+905001000001"; // DevLoginCommand.BarberOwnerDemoPhone

    /// <summary>Gerçek prod ortamını taklit eden minimal IHostEnvironment test double'ı.</summary>
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Kuaforum.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>Hiçbir DevTools bayrağı set EDİLMEMİŞ yapılandırma — üretimdeki varsayılan.</summary>
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    /// <summary>Tek bir anahtarı set eden minimal yapılandırma.</summary>
    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    /// <summary>
    /// DevTools:AllowDemoReset bayrağının VARSAYILAN KAPALI olduğunu ve yalnız AÇIKÇA
    /// true yapıldığında Production korumasını deldiğini sabitler.
    ///
    /// Neden ayrı test: bu bayrak (2026-08-12) founder'ın Render test ortamı için eklendi —
    /// orası ASPNETCORE_ENVIRONMENT=Production ile çalışıyor ama içindeki veri tamamen demo.
    /// Bayrağın sessizce açık gelmesi, gerçek verisi olan bir kurulumda TÜM kullanıcı/randevu/
    /// ödeme verisinin TRUNCATE edilmesi demek olurdu. "Varsayılan kapalı" tam olarak bu
    /// yüzden test edilir — yorumla garanti edilmez.
    /// </summary>
    [Fact]
    public async Task Production_reset_stays_blocked_unless_AllowDemoReset_is_explicitly_true()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var usersBefore = await db.Users.AsNoTracking().CountAsync();
        Assert.True(usersBefore > 0);

        // (a) Bayrak hiç yok → engelli.
        var noFlag = new DemoDataResetter(db, new FakeHostEnvironment(Environments.Production), EmptyConfig(), NullLogger<DemoDataResetter>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noFlag.ResetAsync());

        // (b) Bayrak açıkça false → yine engelli.
        var falseFlag = new DemoDataResetter(db, new FakeHostEnvironment(Environments.Production), ConfigWith("DevTools:AllowDemoReset", "false"), NullLogger<DemoDataResetter>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => falseFlag.ResetAsync());

        // Hiçbir veri kaybolmamalı.
        Assert.Equal(usersBefore, await db.Users.AsNoTracking().CountAsync());

        // (c) Bayrak açıkça true → izin verilir ve TRUNCATE gerçekten çalışır.
        var allowed = new DemoDataResetter(db, new FakeHostEnvironment(Environments.Production), ConfigWith("DevTools:AllowDemoReset", "true"), NullLogger<DemoDataResetter>.Instance);
        await allowed.ResetAsync();
        Assert.Equal(0, await db.Users.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Task madde 1 talebi: "yalnız Development'ta erişilebilir olsun, Production'da
    /// var olmasın/çalışmasın" — bu test DemoDataResetter.ResetAsync'in ÇAĞIRAN TARAF NE
    /// OLURSA OLSUN (route gating'i tamamen atlanan bir senaryo dahi) Production
    /// IHostEnvironment enjekte edildiğinde throw ettiğini ve DB'ye HİÇ dokunmadığını
    /// doğrular — DevEndpoints route gating'inden (env.IsDevelopment() değilken map
    /// edilmiyor) BAĞIMSIZ ikinci bir savunma katmanı.
    /// </summary>
    [Fact]
    public async Task ResetAsync_WhenHostEnvironmentIsProduction_ThrowsAndLeavesDataUntouched()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await adminSeeder.SeedAsync(default);
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(default);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userCountBefore = await db.Users.AsNoTracking().CountAsync();
        var bookingCountBefore = await db.Bookings.AsNoTracking().CountAsync();
        Assert.True(userCountBefore > 0);
        Assert.True(bookingCountBefore > 0);

        var prodResetter = new DemoDataResetter(db, new FakeHostEnvironment(Environments.Production), EmptyConfig(), NullLogger<DemoDataResetter>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => prodResetter.ResetAsync());
        Assert.Contains("Production", ex.Message);

        var userCountAfter = await db.Users.AsNoTracking().CountAsync();
        var bookingCountAfter = await db.Bookings.AsNoTracking().CountAsync();
        Assert.Equal(userCountBefore, userCountAfter);
        Assert.Equal(bookingCountBefore, bookingCountAfter);
    }

    /// <summary>Sanity: aynı sınıf Development/Staging gibi Production-olmayan bir ortamda serbestçe çalışabilir.</summary>
    [Fact]
    public async Task ResetAsync_WhenHostEnvironmentIsNotProduction_TruncatesDemoTables_ButKeepsGeographyAndPlans()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await adminSeeder.SeedAsync(default);
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(default);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cityCountBefore = await db.Cities.AsNoTracking().CountAsync();
        Assert.True(cityCountBefore >= 81); // 81 il — coğrafya seed'i

        var resetter = new DemoDataResetter(db, new FakeHostEnvironment(Environments.Development), EmptyConfig(), NullLogger<DemoDataResetter>.Instance);
        await resetter.ResetAsync();

        Assert.Equal(0, await db.Users.AsNoTracking().CountAsync());
        Assert.Equal(0, await db.Bookings.AsNoTracking().CountAsync());
        Assert.Equal(0, await db.BarberShops.AsNoTracking().CountAsync());
        // Coğrafya verisine DOKUNULMADI (task kısıtı — pahalı yeniden üretim).
        Assert.Equal(cityCountBefore, await db.Cities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task IsStaleAsync_WhenNeverSeeded_ReturnsFalse()
    {
        await factory.ResetDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var resetter = scope.ServiceProvider.GetRequiredService<DemoDataResetter>();
        Assert.False(await resetter.IsStaleAsync());
    }

    [Fact]
    public async Task IsStaleAsync_WhenDevOwnerShopHasNoBookingToday_ReturnsTrue()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await adminSeeder.SeedAsync(default);
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(default); // gerçek "now" — normal seed
        }

        // Canlıda gerçekten olan şeyi simüle et: zaman ilerler, dün seed edilen "bugünün"
        // randevuları artık geçmişte kalır. Booking.Initiate'in "geçmişte olamaz" guard'ı
        // gerçek DateTimeOffset.UtcNow'a bakıyor (bkz. Booking.cs satır 87) — bu yüzden burada
        // RichDemoSeeder'a sahte bir nowOverride vermek yerine (guard'ı domain seviyesinde
        // reddederdi), zaten üretilmiş randevuları doğrudan SQL ile 400 gün ileri taşıyoruz —
        // "artık hiçbiri bugün değil" durumunu domain kurallarını bypass etmeden üretir.
        using (var mutateScope = factory.Services.CreateScope())
        {
            var mutateDb = mutateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await mutateDb.Database.ExecuteSqlRawAsync(
                "UPDATE bookings SET scheduled_at = scheduled_at + interval '400 days', scheduled_ends_at = scheduled_ends_at + interval '400 days'");
        }

        using var scope2 = factory.Services.CreateScope();
        var resetter = scope2.ServiceProvider.GetRequiredService<DemoDataResetter>();
        Assert.True(await resetter.IsStaleAsync());
    }

    [Fact]
    public async Task IsStaleAsync_WhenDevOwnerShopHasBookingToday_ReturnsFalse()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await adminSeeder.SeedAsync(default);
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(default); // gerçek "now" — bugüne çapalı randevular üretir
        }

        using var scope2 = factory.Services.CreateScope();
        var resetter = scope2.ServiceProvider.GetRequiredService<DemoDataResetter>();
        Assert.False(await resetter.IsStaleAsync());
    }

    /// <summary>
    /// Task madde 4/5: forceReset ile üst üste iki tazeleme çift kayıt ÜRETMEMELİ (her
    /// seferinde TRUNCATE + sıfırdan seed — additive DEĞİL, idempotent bir "reset" operasyonu).
    /// Ayrıca ikinci koşum sonrası dev-login "BarberOwner" personasının salonunda BUGÜNE ait
    /// en az bir randevu (owner paneli boş kalmamalı) olduğunu doğrular.
    /// </summary>
    [Fact]
    public async Task SeedReferenceDataAsync_ForceReset_RunTwice_DoesNotDuplicate_AndProducesTodayAnchoredData()
    {
        await factory.ResetDatabaseAsync();

        await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
            factory.Services, seedDemoBarbers: true, seedComprehensiveDemo: false, seedRichDemo: true,
            isProduction: false, forceReset: true);

        int userCount1, shopCount1, erzurumShopCount1;
        using (var db1 = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>())
        {
            userCount1 = await db1.Users.AsNoTracking().CountAsync(u => u.PhoneE164 == RichGatePhone);
            shopCount1 = await db1.BarberShops.AsNoTracking().CountAsync();
            var erzurum1 = await db1.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
            erzurumShopCount1 = await db1.BarberShops.AsNoTracking().CountAsync(s => s.AddressCityId == erzurum1.Id);
        }

        Assert.Equal(1, userCount1);
        Assert.InRange(erzurumShopCount1, 12, 15);

        // İkinci tazeleme — aynı process, gerçek "now" (bir gün ileri sarmaya gerek yok, aynı
        // günde de forceReset her zaman TRUNCATE+reseed yapar).
        await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
            factory.Services, seedDemoBarbers: true, seedComprehensiveDemo: false, seedRichDemo: true,
            isProduction: false, forceReset: true);

        using var db2 = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userCount2 = await db2.Users.AsNoTracking().CountAsync(u => u.PhoneE164 == RichGatePhone);
        var shopCount2 = await db2.BarberShops.AsNoTracking().CountAsync();
        var erzurum2 = await db2.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
        var erzurumShopCount2 = await db2.BarberShops.AsNoTracking().CountAsync(s => s.AddressCityId == erzurum2.Id);

        Assert.Equal(userCount1, userCount2); // 1 — NOT 2
        Assert.Equal(shopCount1, shopCount2); // toplam salon sayısı sabit — NOT ikiye katlanmadı
        Assert.Equal(erzurumShopCount1, erzurumShopCount2);

        // Owner paneli boş kalmamalı: dev-login "BarberOwner" salonunda BUGÜNE ait randevu.
        var devOwner = await db2.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DevLoginOwnerPhone);
        var devShop = await db2.BarberShops.AsNoTracking().FirstAsync(s => s.OwnerId == devOwner.Id);
        var istanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, istanbulTz).DateTime);
        var devBookings = await db2.Bookings.AsNoTracking().Where(b => b.BarberShopId == devShop.Id).ToListAsync();
        Assert.Contains(devBookings, b =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.ScheduledAt, istanbulTz).DateTime) == todayLocal);

        // Owner + admin faturalama ekranları boş kalmamalı.
        var devInvoices = await db2.SubscriptionInvoices.AsNoTracking().Where(s => s.BarberShopId == devShop.Id).ToListAsync();
        Assert.NotEmpty(devInvoices);
    }

    /// <summary>
    /// Task madde 2: otomatik bayatlama tespiti — forceReset OLMADAN, sadece stale veri
    /// SeedReferenceDataAsync'in kendi IsStaleAsync kontrolüyle yakalanıp tazelenmeli
    /// (founder her sabah restart/endpoint tetiklemeden de bu yoldan kurtulur).
    /// </summary>
    [Fact]
    public async Task SeedReferenceDataAsync_AutoDetectsStaleData_WithoutForceReset_AndRefreshes()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await adminSeeder.SeedAsync(default);
            var rich = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await rich.SeedAsync(default); // gerçek "now" ile normal seed
        }

        // Bkz. IsStaleAsync_WhenDevOwnerShopHasNoBookingToday_ReturnsTrue XML doc'u — zamanın
        // ilerlemesini SQL ile simüle ediyoruz (domain guard'ını bypass etmeden).
        using (var mutateScope = factory.Services.CreateScope())
        {
            var mutateDb = mutateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await mutateDb.Database.ExecuteSqlRawAsync(
                "UPDATE bookings SET scheduled_at = scheduled_at + interval '400 days', scheduled_ends_at = scheduled_ends_at + interval '400 days'");
        }

        // forceReset:false — yalnız otomatik IsStaleAsync tespiti tetiklenmeli.
        await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
            factory.Services, seedDemoBarbers: true, seedComprehensiveDemo: false, seedRichDemo: true,
            isProduction: false, forceReset: false);

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var devOwner = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DevLoginOwnerPhone);
        var devShop = await db.BarberShops.AsNoTracking().FirstAsync(s => s.OwnerId == devOwner.Id);
        var istanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, istanbulTz).DateTime);
        var devBookings = await db.Bookings.AsNoTracking().Where(b => b.BarberShopId == devShop.Id).ToListAsync();
        Assert.Contains(devBookings, b =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.ScheduledAt, istanbulTz).DateTime) == todayLocal);
    }

    /// <summary>Task madde 3: admin billing settings ekranı IBAN/banka/hesap sahibi DOLU olmalı.</summary>
    [Fact]
    public async Task SeedReferenceDataAsync_SeedsDemoBankingInfo_WhenDemoFlagsOn()
    {
        await factory.ResetDatabaseAsync();
        await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
            factory.Services, seedDemoBarbers: true, seedComprehensiveDemo: false, seedRichDemo: true,
            isProduction: false, forceReset: true);

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settings = await db.Set<Kuaforum.Domain.Modules.Billing.PlatformSettings>().AsNoTracking().FirstAsync();
        Assert.False(string.IsNullOrWhiteSpace(settings.CommissionPaymentIban));
        Assert.False(string.IsNullOrWhiteSpace(settings.CommissionPaymentBankName));
        Assert.False(string.IsNullOrWhiteSpace(settings.CommissionPaymentAccountHolderName));
    }
}
