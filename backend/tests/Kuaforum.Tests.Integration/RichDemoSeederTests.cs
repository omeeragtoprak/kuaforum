using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RichDemoSeeder (Development-only "founder gezecek" seeder — 7 yeni Erzurum salonu,
/// her randevu durumu, yorum havuzu, faturalama örnekleri). IntegrationTestFactory
/// Seed:RichDemo'yu "false"a sabitliyor (bkz. ConfigureWebHost), yani bu seeder yalnız
/// burada EXPLICIT çağrıldığında çalışır — 170 mevcut testin hiçbiri ondan etkilenmez.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RichDemoSeederTests(IntegrationTestFactory factory) : IAsyncLifetime
{
    private const string DemoCustomerPhone = "+905551234567";
    private const string DemoPaidPlanCode = "DEMO_PAID_QA"; // RichDemoSeeder.DemoPaidPlanCode ile birebir aynı

    // subscription_plans/plan_entitlements ResetDatabaseAsync'in TRUNCATE listesinde YOK
    // (bkz. EntitlementServiceTests XML doc) — RichDemoSeeder'ın oluşturduğu demo/QA plan satırı
    // her testten sonra AÇIKÇA temizlenmeli, aksi halde SubscriptionPlanTests.
    // SubscriptionPlanSeeder_RunTwice_DoesNotDuplicateRows gibi "toplam plan sayısı 3" varsayan
    // testlere sızar.
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shop_subscriptions WHERE plan_code = {DemoPaidPlanCode}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM subscription_plans WHERE code = {DemoPaidPlanCode}");
    }

    [Fact]
    public async Task SeedAsync_CreatesRichErzurumData_AllBookingStatusesRepresented_AndBillingExamples()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default, SeedNow);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var erzurum = await db.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
        var erzurumShops = await db.BarberShops.AsNoTracking()
            .Where(s => s.AddressCityId == erzurum.Id)
            .ToListAsync();
        // 6 (DemoBarberSeeder) + 7 (RichDemoSeeder) = 13 — "Erzurum ağırlıklı ~12-15" hedefi.
        Assert.InRange(erzurumShops.Count, 12, 15);

        var depositShops = erzurumShops.Where(s => s.BookingPolicyDepositEnabled).ToList();
        Assert.Equal(2, depositShops.Count);
        Assert.Contains(depositShops, s => s.BookingPolicyDepositPercent == 0.10m);
        Assert.Contains(depositShops, s => s.BookingPolicyDepositPercent == 0.20m);
        Assert.Contains(erzurumShops, s => s.BookingPolicyMinDaysBetweenBookings == 7 && s.BookingPolicyMaxBookingsPerMonth == 2);

        var genderTypes = erzurumShops.Select(s => s.GenderType).Distinct().ToList();
        Assert.Contains(SalonGenderType.MaleOnly, genderTypes);
        Assert.Contains(SalonGenderType.FemaleOnly, genderTypes);
        Assert.Contains(SalonGenderType.Unisex, genderTypes);

        // Demo Müşteri persona (dev-login "Customer") — HER randevu durumu temsil edilsin.
        var demoCustomer = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DemoCustomerPhone);
        var demoBookings = await db.Bookings.AsNoTracking().Where(b => b.CustomerId == demoCustomer.Id).ToListAsync();
        var statuses = demoBookings.Select(b => b.Status).ToHashSet();
        Assert.Contains(BookingStatus.Completed, statuses);
        Assert.Contains(BookingStatus.Cancelled, statuses);
        Assert.Contains(BookingStatus.NoShow, statuses);
        Assert.Contains(BookingStatus.Confirmed, statuses);
        Assert.Contains(BookingStatus.Pending, statuses);

        // En az bir Pending randevu gerçekten kapora ödenmiş olmalı (DepositPaidAt dolu +
        // ilişkili Payment Captured) — C4/C5c akışının UI'da görünür olması için kritik.
        var pendingWithDeposit = demoBookings.Where(b => b.Status == BookingStatus.Pending && b.DepositPaidAt != null).ToList();
        var depositBooking = Assert.Single(pendingWithDeposit);
        Assert.NotNull(depositBooking.PaymentId);
        var depositPayment = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == depositBooking.PaymentId);
        Assert.Equal(Kuaforum.Domain.Modules.Payment.PaymentStatus.Captured, depositPayment.Status);
        Assert.Equal(Kuaforum.Domain.Modules.Payment.PaymentKind.Deposit, depositPayment.Kind);

        // Plain Pending (ödemesiz, owner onayı bekliyor) da ayrıca var olmalı.
        Assert.Contains(demoBookings, b => b.Status == BookingStatus.Pending && b.DepositPaidAt == null);

        // Reviews — 15-25 hedefi, en az bir Pending moderasyon.
        var reviews = await db.Reviews.AsNoTracking().ToListAsync();
        Assert.InRange(reviews.Count, 10, 40); // geniş tolerans — Rng-bağımlı %75 seçim oranı
        Assert.Contains(reviews, r => r.ModerationStatus == Kuaforum.Domain.Modules.Review.ReviewModerationStatus.Pending);
        Assert.Contains(reviews, r => r.ModerationStatus == Kuaforum.Domain.Modules.Review.ReviewModerationStatus.Approved);

        // Billing — 3 fatura, farklı durumlar (demo/QA paid plan üzerinden — bkz. RichDemoSeeder.DemoPaidPlanCode).
        var invoices = await db.SubscriptionInvoices.AsNoTracking()
            .Where(s => erzurumShops.Select(x => x.Id).Contains(s.BarberShopId))
            .ToListAsync();
        var richInvoices = invoices.Where(s => s.PlanCode == DemoPaidPlanCode).ToList();
        Assert.Equal(3, richInvoices.Count);
        Assert.Contains(richInvoices, s => s.Status == SubscriptionInvoiceStatus.Notified);
        Assert.Contains(richInvoices, s => s.Status == SubscriptionInvoiceStatus.SuspensionApplied);
        Assert.Contains(richInvoices, s => s.Status == SubscriptionInvoiceStatus.Paid);
        var suspendedShopId = richInvoices.Single(s => s.Status == SubscriptionInvoiceStatus.SuspensionApplied).BarberShopId;
        var suspendedShop = erzurumShops.Single(s => s.Id == suspendedShopId);
        Assert.Equal(BarberShopStatus.Suspended, suspendedShop.Status);

        // Notifications — mixed read/unread for Demo Müşteri.
        var notifs = await db.NotificationLogs.AsNoTracking().Where(n => n.UserId == demoCustomer.Id).ToListAsync();
        Assert.Contains(notifs, n => n.IsRead);
        Assert.Contains(notifs, n => !n.IsRead);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_SecondRunDoesNotDuplicateData()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default, SeedNow);
            await seeder.SeedAsync(default); // second run — must be a no-op
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var erzurum = await db.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
        var erzurumShopCount = await db.BarberShops.AsNoTracking().CountAsync(s => s.AddressCityId == erzurum.Id);
        Assert.InRange(erzurumShopCount, 12, 15); // NOT doubled

        var ownerCount = await db.Users.AsNoTracking().CountAsync(u => u.PhoneE164 == "+905002000001");
        Assert.Equal(1, ownerCount);
    }

    /// <summary>
    /// Regresyon kilidi (canlı dev DB'de yakalanan gerçek bug, 2026-08-06): "+905551234567"
    /// (dev-login "Customer" personası) daha önce bir hesap-silme akışı testinde soft-delete
    /// edilmişse, seeder onu sessizce OLDUĞU GİBİ kullanıyordu — User.CanLogin=false olduğu
    /// için dev-login sonsuza dek 400 dönüyor, üretilen tüm zengin randevu/yorum/bildirim
    /// verisi hiç görülemiyordu. Fix: RestoreFromSoftDelete() (30 günlük pencere).
    /// </summary>
    [Fact]
    public async Task SeedAsync_WhenDemoCustomerWasSoftDeleted_RestoresAccount_SoDevLoginWorksAfterward()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var phoneRes = Kuaforum.Domain.Modules.Identity.ValueObjects.PhoneE164.TryCreate(DemoCustomerPhone);
            var userRes = User.CreateCustomerByPhone(phoneRes.Value, "Demo Müşteri");
            var user = userRes.Value;
            Assert.True(user.SoftDelete().IsSuccess);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default);
        }

        using var db2 = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var demoCustomer = await db2.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DemoCustomerPhone);
        Assert.True(demoCustomer.CanLogin, "Soft-delete edilmiş Demo Müşteri hesabı RichDemoSeeder sonrası hâlâ giriş yapamıyor.");
        Assert.Null(demoCustomer.DeletedAt);

        // Zengin veri gerçekten BU (geri yüklenmiş) kullanıcıya bağlanmış olmalı.
        var demoBookings = await db2.Bookings.AsNoTracking().Where(b => b.CustomerId == demoCustomer.Id).ToListAsync();
        Assert.NotEmpty(demoBookings);
    }

    /// <summary>
    /// Regresyon kilidi: RichDemoSeeder'ı ekleyen değişiklik, mevcut DemoBarberSeeder-bazlı
    /// testlerin varsaydığı "tam 6 demo shop" davranışını BOZMAMALI — bu seeder yalnız
    /// explicit çağrıldığında ek şablar üretir, DemoBarberSeeder tek başına çalıştığında hâlâ
    /// yalnızca kendi 6 (+ 7 diğer şehir) şablonunu üretir.
    /// </summary>
    [Fact]
    public async Task DemoBarberSeeder_Alone_StillProducesOnlySixErzurumShops_WhenRichDemoSeederNeverCalled()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var erzurum = await db.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
        var erzurumShopCount = await db.BarberShops.AsNoTracking().CountAsync(s => s.AddressCityId == erzurum.Id);
        Assert.Equal(6, erzurumShopCount);
    }

    private const string DevLoginOwnerPhone = "+905001000001"; // DevLoginCommand.BarberOwnerDemoPhone ile birebir aynı

    /// <summary>
    /// Seed referans anı: iş saatleri ORTASI (Europe/Istanbul 13:00). Hem geçmiş
    /// (Completed) hem gelecek (Confirmed/Pending) slot üretilebilsin diye seçildi.
    /// Not: `SeedAsync` bu değeri girişte `ToUniversalTime()` ile normalize eder —
    /// yerel offset'li bir değer doğrudan `timestamptz`'e yazılamaz.
    /// </summary>
    private static readonly DateTimeOffset SeedNow = BuildSeedNow();

    /// <summary>
    /// BUGÜNÜN öğle vakti (Europe/Istanbul 13:00). Sabit bir TARİH kullanılamaz: seeder
    /// randevuları "bugün"e göre yerleştiriyor ve test de "bugün"ü kontrol ediyor; geçmiş
    /// bir güne sabitlemek ikisini ayrıştırır. Sabitlenen yalnız SAAT — böylece test
    /// mesai dışında (19:00 sonrası) koşsa da seed öğlen yapılmış gibi davranır ve
    /// hem geçmiş (Completed) hem gelecek (Pending/Confirmed) slot üretilebilir.
    /// </summary>
    private static DateTimeOffset BuildSeedNow()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var todayLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date;
        var noon = new DateTime(todayLocal.Year, todayLocal.Month, todayLocal.Day, 13, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(noon, tz.GetUtcOffset(noon)).ToUniversalTime();
    }

    /// <summary>
    /// Regresyon kilidi (elle test bulgusu, 2026-08-06): dev-login "BarberOwner" personasının
    /// salonu ("Modern Stüdyo Kuaför") RichDemoSeeder'ın ürettiği hiçbir randevuya sahip
    /// değildi — panel tamamen boş görünüyordu. Bu test bugün en az bir Completed ve bir
    /// Pending randevu olduğunu, saatlerin iş saatleri içinde kaldığını, faturalama ve KYC
    /// kuyruğunun da dolu olduğunu doğrular.
    ///
    /// SAAT SABİTLENDİ (2026-08-11): önceden gerçek `UtcNow` ile koşuyordu ve **mesai
    /// dışında (19:00 sonrası) düzenli olarak kırılıyordu** — akşam seed edildiğinde
    /// bugüne ait GELECEK bir slot kalmadığı için Pending/Confirmed randevu üretilemez.
    /// Bu, seeder'ın kusuru değil bilinçli davranışı: "çakışan/iş saati dışı randevu
    /// üretmektense eksik üretmek yeğdir" (bkz. RichDemoSeeder `NextSlotIndexAfterNow`).
    /// Yani KIRILAN TESTTİ, seeder değil.
    ///
    /// Artık `nowOverride` ile öğlene sabitleniyor: iddia deterministik olarak
    /// doğrulanabiliyor. Gerçek-saat davranışı ayrı bir testle zaten kapsanıyor
    /// (`SeedAsync_AtVariousLocalHours_...` — 03:00/08:00/13:00/18:30/23:00 vakalarını
    /// koşturup çakışma olmadığını doğruluyor), yani bu değişiklik kapsam kaybı değil.
    ///
    /// BİLİNEN VE KABUL EDİLMİŞ DAVRANIŞ: dev ortamı akşam saatlerinde ayağa kaldırılırsa
    /// kuaförün "Bugün" ekranı daha az randevu gösterir (yalnız geçmiş/Completed). Sabah
    /// tazeleme (`DemoDataResetter`) bunu ertesi gün düzeltir.
    /// </summary>
    [Fact]
    public async Task SeedAsync_DevLoginOwnerShop_HasTodayBookings_Billing_AndAdminKycQueueIsPopulated()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            // Production sırasını (SeedReferenceDataAsync: AdminSeeder → ... → RichDemoSeeder)
            // yansıtmak için AdminSeeder de burada açıkça çağrılıyor — ResetDatabaseAsync
            // "users" tablosunu TRUNCATE ettiğinden factory boot'undaki admin de siliniyor;
            // aksi halde Step 10'un UnderReview geçişi (reviewer id gerektirir) admin
            // bulamayıp sessizce Pending'de kalır.
            var adminSeeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.AdminSeeder>();
            await adminSeeder.SeedAsync(default);

            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var devOwner = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DevLoginOwnerPhone);
        var devShop = await db.BarberShops.AsNoTracking().FirstAsync(s => s.OwnerId == devOwner.Id);
        Assert.Equal("Modern Stüdyo Kuaför", devShop.Name);

        var istanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(SeedNow, istanbulTz).DateTime);

        var devBookings = await db.Bookings.AsNoTracking().Where(b => b.BarberShopId == devShop.Id).ToListAsync();
        Assert.NotEmpty(devBookings);

        bool IsToday(Kuaforum.Domain.Modules.Booking.Booking b) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.ScheduledAt, istanbulTz).DateTime) == todayLocal;

        var todaysBookings = devBookings.Where(IsToday).ToList();
        Assert.Contains(todaysBookings, b => b.Status == BookingStatus.Completed);
        Assert.Contains(todaysBookings, b => b.Status == BookingStatus.Pending);
        Assert.True(todaysBookings.Count >= 5, $"Bugün için en az 5 randevu bekleniyordu, {todaysBookings.Count} bulundu.");

        // Kusur 2 regresyon kilidi: HİÇBİR randevu iş saatleri (09:00-19:00 Europe/Istanbul,
        // makul bir payla) dışında olmamalı — önceki sürüm sunucunun o anki saat-dakikasını
        // (ör. 06:58) sabitliyordu. "Bugünün" 2 Confirmed + 1 Pending'i seed zamanının GERÇEK
        // saatine göre ("şimdiden X dk sonra" sıralı kursör, bkz. RichDemoSeeder Step 9 madde 2)
        // üretildiği için akşamüzeri/geç saatte seed edilirse 19:00'ı birazcık aşabilir — bu
        // yüzden bugünün randevularına 21:00'a kadar makul bir pay tanınır; gelecek günlerin
        // randevuları (RealisticSlotUtc ile üretilir, "now"dan bağımsız) SIKI 09:00-19:00 sınırına tabidir.
        foreach (var b in devBookings)
        {
            var localTime = TimeZoneInfo.ConvertTime(b.ScheduledAt, istanbulTz).TimeOfDay;
            var upperBound = IsToday(b) ? TimeSpan.FromHours(21) : TimeSpan.FromHours(19);
            Assert.True(localTime >= TimeSpan.FromHours(9) && localTime <= upperBound,
                $"Randevu {b.Id} iş saatleri dışında: {localTime} (ScheduledAt UTC={b.ScheduledAt}).");
        }

        // En az bir kapora ödenmiş Pending randevu (dev-login owner'ın salonunda da).
        var devDeposit = devBookings.Where(b => b.Status == BookingStatus.Pending && b.DepositPaidAt != null).ToList();
        Assert.NotEmpty(devDeposit);
        Assert.True(devShop.BookingPolicyDepositEnabled, "Kapora ödemesi olan bir randevu var ama salonun kapora ayarı kapalı.");

        // Faturalama — owner ekranı boş kalmamalı.
        var devInvoices = await db.SubscriptionInvoices.AsNoTracking().Where(s => s.BarberShopId == devShop.Id).ToListAsync();
        Assert.Contains(devInvoices, s => s.Status == SubscriptionInvoiceStatus.Notified);
        Assert.Contains(devInvoices, s => s.Status == SubscriptionInvoiceStatus.Paid);

        // Bildirimler — owner'a en az bir bildirim.
        var devNotifs = await db.NotificationLogs.AsNoTracking().Where(n => n.UserId == devOwner.Id).ToListAsync();
        Assert.NotEmpty(devNotifs);

        // Admin KYC kuyruğu — daha önce her zaman boştu (hiçbir seeder KycSubmission satırı
        // üretmiyordu). En az 2 kayıt: biri Pending, biri UnderReview.
        var kycSubmissions = await db.KycSubmissions.AsNoTracking().ToListAsync();
        Assert.True(kycSubmissions.Count >= 2, $"Admin KYC kuyruğunda en az 2 kayıt bekleniyordu, {kycSubmissions.Count} bulundu.");
        Assert.Contains(kycSubmissions, k => k.Status == KycStatus.Pending);
        Assert.Contains(kycSubmissions, k => k.Status == KycStatus.UnderReview);

        // BarberOwnerDraft personasının kendi KYC akışı hâlâ TAZE olmalı (Step 10 ona dokunmamalı).
        var draftOwner = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == "+905001000099");
        var draftShop = await db.BarberShops.AsNoTracking().FirstAsync(s => s.OwnerId == draftOwner.Id);
        Assert.Equal(BarberShopStatus.Draft, draftShop.Status);
        var draftSubmissions = await db.KycSubmissions.AsNoTracking().Where(k => k.BarberShopId == draftShop.Id).ToListAsync();
        Assert.Empty(draftSubmissions);
    }

    /// <summary>
    /// Review bulgusu (2026-08-07): yerel saat ≤08:40 iken (mesai açılışına çok yakın)
    /// dev-login owner şablonunun "bugün" Confirmed/Pending imleci (stylist0Slot/stylist1Slot,
    /// bkz. RichDemoSeeder Step 9) doğrudan 0'dan başlıyordu — ama o stiliste ZATEN slot 0'da
    /// (stylist 0) veya Math.Min(pastIdxToday,4)'te (stylist 1) bir Completed randevu (todayCompleted1/2)
    /// yerleştirilmişti. ex_bookings_stylist_slot EXCLUDE constraint'i (bkz. M15 migration,
    /// predicate yalnız status IN (Pending, Confirmed, Started)) Completed'ı kapsamadığı için
    /// bu çakışma veritabanı tarafından REDDEDİLMİYORDU — sessizce çakışan demo verisi üretiliyordu.
    /// Fix: stylist imleci artık o stiliste bugün zaten yerleştirilmiş randevunun bittiği slotun
    /// ötesinden başlıyor (bkz. RichDemoSeeder.SeedAsync — stylist0OccupiedUntil/stylist1OccupiedUntil).
    ///
    /// SeedAsync artık test-only bir <c>nowOverride</c> parametresi kabul ediyor (production/dev
    /// davranışı değişmedi — null iken gerçek UtcNow kullanılır) ki bu test günün beş farklı
    /// saatinde (mesai öncesi/açılışa çok yakın/öğle/kapanışa yakın/gece) sabit zaman enjekte
    /// ederek çalıştırabilsin. Europe/Istanbul 2016'dan beri yaz saati uygulamıyor (sabit UTC+3),
    /// bu yüzden nowOverride'ı doğrudan +03:00 offset'iyle kurmak IstanbulTz dönüşümüyle birebir eşleşir.
    ///
    /// Assertion status'tan BAĞIMSIZ (Completed dahil) — Confirmed/Pending-Confirmed/Pending
    /// çakışmaları zaten EXCLUDE constraint'i tarafından SaveChangesAsync sırasında exception
    /// olarak yakalanır (bu test o durumda da fail eder, ama farklı bir yoldan); bu test'in asıl
    /// hedefi constraint'in KAPSAMADIĞI Completed-vs-yeni-randevu çakışmasını yakalamak.
    /// </summary>
    [Theory]
    [InlineData(3, 0)]   // gece — mesai çok uzakta
    [InlineData(8, 0)]   // mesai açılışına yakın ama henüz açık değil (orijinal bug pencere)
    [InlineData(13, 0)]  // öğle — mesai ortası
    [InlineData(18, 30)] // kapanışa yakın
    [InlineData(23, 0)]  // gece geç — kapanış çoktan geçmiş
    public async Task SeedAsync_AtVariousLocalHours_NeverProducesOverlappingBookingsForSameStylistToday(int localHour, int localMinute)
    {
        await factory.ResetDatabaseAsync();
        // Europe/Istanbul sabit UTC+3 (2016'dan beri DST yok) — bkz. metod XML doc.
        var nowOverride = new DateTimeOffset(2026, 8, 10, localHour, localMinute, 0, TimeSpan.FromHours(3));

        using (var scope = factory.Services.CreateScope())
        {
            var adminSeeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.AdminSeeder>();
            await adminSeeder.SeedAsync(default);

            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default, nowOverride);
        }

        using var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var devOwner = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneE164 == DevLoginOwnerPhone);
        var devShop = await db.BarberShops.AsNoTracking().FirstAsync(s => s.OwnerId == devOwner.Id);

        var devBookings = await db.Bookings.AsNoTracking()
            .Where(b => b.BarberShopId == devShop.Id)
            .ToListAsync();
        Assert.NotEmpty(devBookings);

        var overlaps = (
            from a in devBookings
            from b in devBookings
            where a.Id != b.Id && a.StylistId == b.StylistId
                  && a.ScheduledAt < b.ScheduledEndsAt && b.ScheduledAt < a.ScheduledEndsAt
            select $"{a.Id}({a.Status})@{a.ScheduledAt:O}..{a.ScheduledEndsAt:O} vs {b.Id}({b.Status})@{b.ScheduledAt:O}..{b.ScheduledEndsAt:O}")
            .ToList();

        Assert.True(overlaps.Count == 0, $"nowOverride={nowOverride:O} — çakışan randevu(lar): {string.Join(" | ", overlaps)}");
    }
}
