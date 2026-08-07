using System.Reflection;
using Kuaforum.Application.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Geography;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Domain.Modules.Notification;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Domain.Modules.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Development-only "founder gezecek" seeder — telefonda/emülatörde her ekran dolu
/// görünsün diye DemoBarberSeeder'ın 6 Erzurum salonunun ÜZERİNE 7 yeni Erzurum salonu
/// ekler (toplam 13, "Erzurum ağırlıklı ~12-15" hedefi), dev quick-login "Customer"
/// personasına (+905551234567, "Demo Müşteri") HER randevu durumunu (Completed/Cancelled/
/// NoShow/Confirmed/Pending/Pending+kapora-ödenmiş) kazandırır, 13 salona yorum havuzu +
/// birkaç owner cevabı + 1-2 moderasyon bekleyen yorum, 3 salona aylık komisyon faturası
/// (Notified/Overdue→SuspensionApplied/Paid) ve birkaç bildirim (okunmuş/okunmamış) ekler.
///
/// Idempotent: bu seeder'a özel ilk owner telefonu (RichShopOwnerPhones[0]) zaten varsa
/// hiç dokunmadan döner. Bilerek CustomerDemoPhone'u (dev-login "Customer") GATE OLARAK
/// KULLANMAZ — o kullanıcı bu seeder hiç çalışmadan ÖNCE bile (developer dev-login yaparsa)
/// var olabilir; onu idempotency anahtarı yapmak "flag sonradan açıldı ama seed hiç
/// çalışmadı" sessiz bug'ına yol açardı.
///
/// PRODUCTION: Bu seeder asla çalışmamalı — yalnız Seed:RichDemo=true iken (appsettings.
/// Development.json'da varsayılan true, appsettings.Production.json'da açıkça false).
/// </summary>
public sealed class RichDemoSeeder(
    ApplicationDbContext db,
    DemoBarberSeeder demoBarberSeeder,
    ILogger<RichDemoSeeder> logger)
{
    private const string CustomerDemoPhone = "+905551234567"; // DevLoginCommand.CustomerDemoPhone ile birebir aynı
    private const decimal CommissionRate = 0.10m; // yalnız Booking.PlatformCommissionMinor (vestigial) demo verisi için — bkz. PaymentAmounts XML doc

    /// <summary>
    /// RFC-014 §2 D1: gerçek STARTER/PRO fiyatı henüz founder kararı DEĞİL (bkz.
    /// SubscriptionPlanSeeder — PriceMinor=null, IsActive=false). Owner/admin faturalama
    /// ekranları BOŞ KALMASIN diye (founder elle test ediyor) burada YALNIZCA demo/QA amaçlı,
    /// ayrı bir plan kodu kullanılır — STARTER/PRO'nun kendi tanımına dokunulmaz, dolayısıyla
    /// SubscriptionPlanSeeder'ın idempotent "if (!exists)" kontrolüyle de hiç çakışmaz (farklı
    /// Code, seeder sırasından bağımsız).
    /// </summary>
    private const string DemoPaidPlanCode = "DEMO_PAID_QA";

    /// <summary>[unverified 2026-08-06 — yalnızca demo/QA gösterge tutarı, gerçek STARTER/PRO fiyatlandırma kararı DEĞİL.]</summary>
    private const long DemoPaidPlanPriceMinor = 149_900; // 1.499,00 TL

    // Kusur 1 fix (2026-08-06, elle test bulgusu): dev-login "BarberOwner" DevLoginCommand.
    // BarberOwnerDemoPhone ile bu telefonu arar → DemoBarberSeeder.DemoData[0] "Modern Stüdyo
    // Kuaför" (Kadıköy/İstanbul). Bu seeder o ana kadar SADECE Erzurum'a odaklıydı — dev-login
    // ile giren kuaförün paneli (bugün/bu hafta/takvim/faturalama/bildirim/yorum) TAMAMEN BOŞ
    // görünüyordu. Şablonun kendisi hard-code EDİLMEZ — owner bu telefondan bulunur, sonra
    // BarberShop.OwnerId ile şablona gidilir (DevLoginCommand'ın kendi arama mantığıyla birebir
    // aynı — id sabitlenirse iki yer birbirinden bağımsız kayarsa sessizce kırılır).
    private const string DevLoginOwnerPhone = "+905001000001";

    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeOnly DefaultOpenTime = new(9, 0);
    private static readonly TimeOnly DefaultCloseTime = new(19, 0);

    // Instance field (BİLEREK static DEĞİL) — RichDemoSeeder DI'da Scoped, her SeedAsync
    // çağrısı taze bir instance üzerinde çalışır. static olsaydı aynı test process'inde
    // ÖNCE koşan başka bir test (ör. idempotency testi SeedAsync'i 2 kez çağırıyor)
    // paylaşılan Random'ın durumunu tüketir, xUnit'in garanti etmediği test sırasına göre
    // sonraki testlerin rastgele çıktıları DEĞİŞİR (adversarial kanıt: full suite koşumunda
    // "en az 1 Pending review" assertion'ı sıraya göre flaky çıktı, izole koşuda geçti).
    private readonly Random Rng = new(2026);

    private static readonly string[] RichShopOwnerPhones =
    {
        "+905002000001", "+905002000002", "+905002000003", "+905002000004",
        "+905002000005", "+905002000006", "+905002000007",
    };

    /// <param name="ct">İptal token'ı.</param>
    /// <param name="nowOverride">
    /// Test-only: bugünkü randevu yerleşimini süren "now" referansını sabitler (bkz.
    /// <see cref="NextSlotIndexAfterNow"/>/<see cref="PastSlotIndexBeforeNow"/>/<see
    /// cref="RealisticSlotUtc"/>). Null iken gerçek <see cref="DateTimeOffset.UtcNow"/>
    /// kullanılır (production/dev davranışı değişmez). Regresyon testi bunu günün farklı
    /// saatlerinde (mesai açılışına çok yakın dahil) çakışan randevu üretilmediğini
    /// doğrulamak için enjekte eder — review bulgusu (2026-08-07): saat ≤08:40 iken
    /// stylist slot imleci 0'dan başlıyor ve o stiliste ZATEN yerleştirilmiş bir Completed
    /// randevuyla (aynı gün, aynı stilist) çakışıyordu.
    /// </param>
    public async Task SeedAsync(CancellationToken ct = default, DateTimeOffset? nowOverride = null)
    {
        var alreadySeeded = await db.Users.AnyAsync(u => u.PhoneE164 == RichShopOwnerPhones[0], ct);
        if (alreadySeeded)
        {
            logger.LogDebug("RichDemoSeeder: already seeded, skipping.");
            return;
        }

        // Base 6 Erzurum shops must exist first — idempotent no-op if DemoBarberSeeder
        // already ran (Program.cs normally orders this correctly; defensive here in case
        // Seed:RichDemo=true is flipped without Seed:Demo=true).
        await demoBarberSeeder.SeedAsync(ct);

        logger.LogInformation("RichDemoSeeder: starting...");

        var erzurum = await db.Cities.FirstOrDefaultAsync(c => c.PlateCode == 25, ct);
        if (erzurum is null)
        {
            logger.LogWarning("RichDemoSeeder: Erzurum (plate 25) not found, aborting.");
            return;
        }

        var districts = await db.Districts.AsNoTracking()
            .Where(d => d.CityId == erzurum.Id)
            .ToDictionaryAsync(d => d.Name, ct);

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null)
            logger.LogWarning("RichDemoSeeder: PlatformSettings missing, booking-policy assignment will be skipped.");

        // ── Step 1: 7 new Erzurum shops ─────────────────────────────────
        var newShops = new List<(BarberShop Shop, List<Stylist> Stylists, List<ServiceCatalog> Services)>();

        foreach (var demo in NewErzurumShops)
        {
            if (!districts.TryGetValue(demo.DistrictName, out var district))
            {
                logger.LogWarning("RichDemoSeeder: district {District} missing, skipping {Name}.", demo.DistrictName, demo.ShopName);
                continue;
            }

            var ownerPhone = PhoneE164.TryCreate(demo.OwnerPhone);
            if (ownerPhone.IsFailure) continue;
            var ownerResult = User.CreateCustomerByPhone(ownerPhone.Value, demo.OwnerName);
            if (ownerResult.IsFailure) continue;
            var owner = ownerResult.Value;
            owner.PromoteToBarberOwner();
            db.Users.Add(owner);

            var shopResult = BarberShop.Create(owner.Id, demo.ShopName, demo.GenderType);
            if (shopResult.IsFailure) continue;
            var shop = shopResult.Value;
            shop.UpdateBasics(name: demo.ShopName, description: demo.Description, genderType: null, phone: demo.ShopPhone);

            var coordResult = Coordinate.TryCreate(demo.Latitude, demo.Longitude);
            if (coordResult.IsSuccess)
            {
                var neighborhood = await db.Neighborhoods.AsNoTracking()
                    .FirstOrDefaultAsync(n => n.DistrictId == district.Id, ct);
                var addressResult = Address.Create(erzurum.Id, district.Id, neighborhood?.Id, demo.AddressLine1, coordResult.Value);
                if (addressResult.IsSuccess) shop.SetAddress(addressResult.Value);
            }
            shop.MarkPendingKyc();
            shop.ApproveKyc(subMerchantKey: $"RICH-SUB-{demo.Slug}");
            db.BarberShops.Add(shop);

            var stylists = new List<Stylist>();
            foreach (var stylistName in demo.Stylists)
            {
                var stResult = Stylist.Create(shop.Id, stylistName);
                if (stResult.IsFailure) continue;
                var stylist = stResult.Value;
                db.Stylists.Add(stylist);
                stylists.Add(stylist);

                foreach (var dow in Enum.GetValues<DayOfWeek>())
                {
                    var isClosed = dow == DayOfWeek.Sunday;
                    var whResult = WorkingHours.Create(stylist.Id, dow, new TimeOnly(9, 0), new TimeOnly(19, 0), isClosed);
                    if (whResult.IsSuccess) db.WorkingHours.Add(whResult.Value);
                }
            }

            var services = new List<ServiceCatalog>();
            foreach (var (name, gender, duration, priceTl) in demo.Services)
            {
                var sResult = ServiceCatalog.Create(shop.Id, name, gender, duration, priceMinor: priceTl * 100, bufferMinutes: 5);
                if (sResult.IsSuccess)
                {
                    db.ServiceCatalogs.Add(sResult.Value);
                    services.Add(sResult.Value);
                }
            }

            newShops.Add((shop, stylists, services));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("RichDemoSeeder: created {N} new Erzurum shops.", newShops.Count);

        if (newShops.Count < 7)
        {
            logger.LogWarning("RichDemoSeeder: expected 7 new shops, got {N} — district/city lookups may be incomplete. Continuing with what was created.", newShops.Count);
        }

        // Walk-in: enable on the first stylist of shop[0] (Lale) and shop[3] (Vadi).
        if (newShops.Count > 0 && newShops[0].Stylists.Count > 0) newShops[0].Stylists[0].ToggleWalkIn(true);
        if (newShops.Count > 3 && newShops[3].Stylists.Count > 0) newShops[3].Stylists[0].ToggleWalkIn(true);

        // Time-off: a future week off for one stylist each on shop[1] (Zirve) and shop[4] (Yıldız).
        // NOT: DateTimeOffset.UtcNow.Date bir DateTime döner (DateTimeOffset DEĞİL) — implicit
        // dönüşüm yerel sistem saat dilimini (bu makinede +03:00) kullanır ve Npgsql
        // "timestamp with time zone" yalnızca Offset=0 kabul ettiği için SaveChangesAsync
        // patlar. new DateTimeOffset(..., TimeSpan.Zero) ile açıkça UTC'ye sabitleniyor
        // (ComprehensiveDemoSeeder'ın da kullandığı kalıp).
        var timeOffStart = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(30);
        if (newShops.Count > 1 && newShops[1].Stylists.Count > 0)
        {
            var toResult = TimeOff.Create(newShops[1].Stylists[0].Id, timeOffStart, timeOffStart.AddDays(5), "Yıllık izin");
            if (toResult.IsSuccess) db.TimeOffs.Add(toResult.Value);
        }
        if (newShops.Count > 4 && newShops[4].Stylists.Count > 0)
        {
            var toResult = TimeOff.Create(newShops[4].Stylists[0].Id, timeOffStart.AddDays(10), timeOffStart.AddDays(13), "Sağlık izni");
            if (toResult.IsSuccess) db.TimeOffs.Add(toResult.Value);
        }

        // Booking policy: shop[0]=10% kapora, shop[1]=20% kapora, shop[2]=anti-spam.
        if (settings is not null)
        {
            if (newShops.Count > 0)
            {
                var p0 = BookingPolicy.Create(true, 0.10m, 2, null, null, settings.DepositPercentMin, settings.DepositPercentMax);
                if (p0.IsSuccess) newShops[0].Shop.SetBookingPolicy(p0.Value);
            }
            if (newShops.Count > 1)
            {
                var p1 = BookingPolicy.Create(true, 0.20m, 2, null, null, settings.DepositPercentMin, settings.DepositPercentMax);
                if (p1.IsSuccess) newShops[1].Shop.SetBookingPolicy(p1.Value);
            }
            if (newShops.Count > 2)
            {
                var p2 = BookingPolicy.Create(false, null, 2, 7, 2, settings.DepositPercentMin, settings.DepositPercentMax);
                if (p2.IsSuccess) newShops[2].Shop.SetBookingPolicy(p2.Value);
            }
        }
        await db.SaveChangesAsync(ct);

        // ── Step 2: demo customers ───────────────────────────────────────
        var demoCustomer = await db.Users.FirstOrDefaultAsync(u => u.PhoneE164 == CustomerDemoPhone, ct);
        if (demoCustomer is null)
        {
            var phoneResult = PhoneE164.TryCreate(CustomerDemoPhone);
            var createResult = phoneResult.IsSuccess ? User.CreateCustomerByPhone(phoneResult.Value, "Demo Müşteri") : null;
            if (createResult is { IsSuccess: true })
            {
                demoCustomer = createResult.Value;
                db.Users.Add(demoCustomer);
                await db.SaveChangesAsync(ct);
            }
        }
        else if (demoCustomer.IsDeleted)
        {
            // Gerçek bug bulundu (live dev DB'de doğrulandı, 2026-08-06): bu telefon daha önce
            // bir hesap-silme akışı testinde soft-delete edilmişse, sessizce silinmiş kullanıcıya
            // zengin randevu/yorum/bildirim verisi bağlamak boşa gider — dev-login "Customer"
            // User.CanLogin=false olduğu için hep 400 döner, founder hiçbir şey GÖREMEZ.
            var restoreResult = demoCustomer.RestoreFromSoftDelete();
            if (restoreResult.IsFailure)
            {
                // 30 günlük geri yükleme penceresi geçmişse (olası ama nadir) yeni bir kullanıcı
                // YARATAMAYIZ (aynı telefon numarası unique) — en azından logla, seeder'ın geri
                // kalanı (13 salon, faturalama, review havuzu) hâlâ değerli, sadece Demo Müşteri'nin
                // KENDİ randevuları görünmez kalır.
                logger.LogWarning("RichDemoSeeder: {Phone} hesabı silinmiş ve geri yüklenemedi ({Reason}) — Demo Müşteri randevuları atlanacak.",
                    CustomerDemoPhone, restoreResult.Error.Message);
                demoCustomer = null;
            }
            else
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("RichDemoSeeder: {Phone} (Demo Müşteri) soft-delete'ten geri yüklendi.", CustomerDemoPhone);
            }
        }

        var secondaryCustomers = new List<User>();
        foreach (var c in SecondaryCustomers)
        {
            var phoneResult = PhoneE164.TryCreate(c.Phone);
            if (phoneResult.IsFailure) continue;
            var userResult = User.CreateCustomerByPhone(phoneResult.Value, c.DisplayName);
            if (userResult.IsFailure) continue;
            db.Users.Add(userResult.Value);
            secondaryCustomers.Add(userResult.Value);
        }
        await db.SaveChangesAsync(ct);

        await GrantTosConsentIfMissingAsync(demoCustomer, ct);
        foreach (var c in secondaryCustomers) await GrantTosConsentIfMissingAsync(c, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("RichDemoSeeder: demo customer ready ({HasDemo}), {N} secondary customers.", demoCustomer is not null, secondaryCustomers.Count);

        // ── Step 3: all 13 Active Erzurum shops (6 existing + 7 new) ────
        var allErzurumShops = await db.BarberShops.AsNoTracking()
            .Where(s => s.AddressCityId == erzurum.Id && s.Status == BarberShopStatus.Active)
            .OrderBy(s => s.Slug)
            .ToListAsync(ct);

        var stylistsByShop = (await db.Stylists.AsNoTracking()
                .Where(s => s.IsActive)
                .ToListAsync(ct))
            .GroupBy(s => s.BarberShopId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var servicesByShop = (await db.ServiceCatalogs.AsNoTracking()
                .Where(s => s.IsActive)
                .ToListAsync(ct))
            .GroupBy(s => s.BarberShopId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // `now`dan türetilen değerler (placeholder randevu saatleri, imleçler) doğrudan DB'ye
        // yazılıyor ve Npgsql `timestamptz` kolonuna YALNIZ offset 0 kabul ediyor. Çağıran
        // taraf yerel offset'li bir değer verirse (testler nowOverride'ı Europe/Istanbul
        // +03:00 ile kuruyor ki iş-saati hesabıyla birebir eşleşsin) kayıt anında
        // "Cannot write DateTimeOffset with Offset=03:00:00" ile patlıyordu. Giriş noktasında
        // UTC'ye normalize ediyoruz; iş-saati hesapları zaten kendi içinde IstanbulTz'e
        // çeviriyor, dolayısıyla davranış değişmiyor.
        var now = (nowOverride ?? DateTimeOffset.UtcNow).ToUniversalTime();

        // Kusur 2 fix (2026-08-06, elle test bulgusu): "todayLocal" + "daysSinceMonday"
        // aşağıdaki RealisticSlotUtc çağrılarının VE dev-login owner'ın "bu hafta/geçen hafta"
        // randevu üretiminin ortak referansı — GetOwnerDashboardStatsQueryHandler.
        // MondayStartUtc ile AYNI formül (((int)DayOfWeek + 6) % 7, Pazartesi=0) kasıtlı
        // tekrarlanıyor: o handler Application katmanında, bu seeder Infrastructure'da,
        // aralarında paylaşılan bir yardımcı yok (BookingPolicy.MaxPendingPerCustomerCeiling /
        // InitiateBookingCommand.MaxActivePendingBookingsPerCustomer'daki bilinçli
        // cross-layer tekrar presedentiyle aynı ilke — PolicyConstantsPinTests o ikisini
        // birbirine pin'ler, burada öyle bir test yok ama formül tek satır ve triviyal).
        var nowLocal = TimeZoneInfo.ConvertTime(now, IstanbulTz);
        var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
        var daysSinceMonday = ((int)todayLocal.DayOfWeek + 6) % 7;

        var bookingsToAdd = new List<Booking>();
        var paymentsToAdd = new List<Payment>();
        var reviewCandidates = new List<Booking>(); // Completed bookings eligible for reviews

        // ── Step 4: Demo Müşteri's explicit 6 bookings (every status) ──
        // Entity.Id is client-generated (Guid.NewGuid() in the base constructor, not a DB
        // default) — the in-memory newShops[i].Stylists/.Services lists from Step 1 already
        // carry real, usable Ids even though SaveChangesAsync hasn't round-tripped since.
        Booking? MakeDemoBooking(int shopIndex, DateTimeOffset scheduledAt, bool requiresOnlinePayment = false, decimal? depositSnapshot = null)
        {
            var (shop, stylists, services) = newShops[shopIndex];
            if (stylists.Count == 0 || services.Count == 0) return null;
            var service = services[0];
            var result = Booking.Initiate(
                demoCustomer!.Id, shop.Id, stylists[0].Id, scheduledAt,
                new (Guid, string, int, int, long)[] { (service.Id, service.Name, service.DurationMinutes, service.BufferMinutes, service.PriceMinor) },
                BookingType.Scheduled, CommissionRate, requiresOnlinePayment, depositPercentSnapshot: depositSnapshot);
            return result.IsSuccess ? result.Value : null;
        }

        if (demoCustomer is not null && newShops.Count >= 7)
        {
            // newShops index: [0]=Lale(10% kapora) [1]=Zirve(20% kapora, review pool'a bırakıldı)
            // [2]=Huzur(anti-spam) [3]=Vadi [4]=Yıldız [5]=Dağ [6]=Taze

            // 1) Pending + kapora ödenmiş — C5c/C4 akışının UI'da görünmesi için kritik.
            var depositBooking = MakeDemoBooking(0, RealisticSlotUtc(todayLocal.AddDays(5), 8, newShops[0].Services.Count > 0 ? newShops[0].Services[0].DurationMinutes : 45), requiresOnlinePayment: true, depositSnapshot: 0.10m);
            if (depositBooking is not null)
            {
                var lale = newShops[0].Shop;
                var depositMinor = Kuaforum.Domain.Common.PaymentAmounts.CalculateDepositMinor(depositBooking.TotalAmountMinor, 0.10m);
                var payment = Payment.Initiate(
                    depositBooking.Id, demoCustomer.Id, lale.Id, depositMinor, platformCommissionMinor: 0,
                    PaymentProviderName.Mock, PaymentKind.Deposit, idempotencyKey: $"rich-deposit-{depositBooking.Id:N}").Value;
                payment.Capture($"RICH-TX-{depositBooking.Id:N}"[..32], depositMinor * 18 / 1000);
                depositBooking.MarkPaymentReceived(payment.Id); // Status STAYS Pending — onay bekliyor
                bookingsToAdd.Add(depositBooking);
                paymentsToAdd.Add(payment);
            }

            // 2) Completed (past ~25 gün önce)
            var completedBooking = MakeDemoBooking(3, now.AddHours(2));
            if (completedBooking is not null)
            {
                var payment = CreateAndCapturePayment(completedBooking, demoCustomer.Id, newShops[3].Shop.Id);
                completedBooking.MarkPaymentReceived(payment.Id);
                completedBooking.OwnerConfirm();
                completedBooking.Start();
                completedBooking.Complete();
                payment.MarkSettled();
                var shift = RealisticSlotUtc(todayLocal.AddDays(-25), 6, newShops[3].Services.Count > 0 ? newShops[3].Services[0].DurationMinutes : 45) - completedBooking.ScheduledAt;
                BackdateBooking(completedBooking, shift, confirmed: true, started: true, completed: true, cancelled: false);
                BackdatePayment(payment, shift);
                bookingsToAdd.Add(completedBooking);
                paymentsToAdd.Add(payment);
                reviewCandidates.Add(completedBooking);
            }

            // 3) Cancelled (past ~10 gün önce, owner henüz onaylamadan müşteri vazgeçti)
            var cancelledBooking = MakeDemoBooking(4, now.AddHours(3));
            if (cancelledBooking is not null)
            {
                cancelledBooking.Cancel(BookingCancellationReason.CustomerRequested);
                var shift = RealisticSlotUtc(todayLocal.AddDays(-10), 10, newShops[4].Services.Count > 0 ? newShops[4].Services[0].DurationMinutes : 45) - now.AddHours(3);
                BackdateBooking(cancelledBooking, shift, confirmed: false, started: false, completed: false, cancelled: true);
                bookingsToAdd.Add(cancelledBooking);
            }

            // 4) NoShow (past ~15 gün önce) — Confirmed sonrası raw-SQL status flip (mevcut test
            // suite'inin de kullandığı kalıp, MarkNoShow'un canlı "now" guard'ını atlamak için).
            var noShowBooking = MakeDemoBooking(5, now.AddHours(4));
            if (noShowBooking is not null)
            {
                noShowBooking.OwnerConfirm();
                var shift = RealisticSlotUtc(todayLocal.AddDays(-15), 4, newShops[5].Services.Count > 0 ? newShops[5].Services[0].DurationMinutes : 45) - now.AddHours(4);
                BackdateBooking(noShowBooking, shift, confirmed: true, started: false, completed: false, cancelled: false);
                bookingsToAdd.Add(noShowBooking);
            }

            // 5) Confirmed (upcoming ~+4 gün)
            var confirmedBooking = MakeDemoBooking(6, RealisticSlotUtc(todayLocal.AddDays(4), 16, newShops[6].Services.Count > 0 ? newShops[6].Services[0].DurationMinutes : 45));
            if (confirmedBooking is not null)
            {
                confirmedBooking.OwnerConfirm();
                bookingsToAdd.Add(confirmedBooking);
            }

            // 6) Pending (upcoming ~+7 gün, owner onayı bekliyor, ödeme yok)
            var pendingBooking = MakeDemoBooking(2, RealisticSlotUtc(todayLocal.AddDays(7), 24, newShops[2].Services.Count > 0 ? newShops[2].Services[0].DurationMinutes : 45));
            if (pendingBooking is not null)
            {
                bookingsToAdd.Add(pendingBooking);
            }

            db.Bookings.AddRange(bookingsToAdd);
            db.Payments.AddRange(paymentsToAdd);
            await db.SaveChangesAsync(ct);

            if (noShowBooking is not null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE bookings SET status = {(int)BookingStatus.NoShow} WHERE id = {noShowBooking.Id}", ct);
            }

            bookingsToAdd.Clear();
            paymentsToAdd.Clear();
        }

        // ── Step 5: review-pool bulk bookings across all 13 shops ──────
        var dayCursor = 0;
        var confirmedExtraCount = 0;
        foreach (var shopRow in allErzurumShops)
        {
            if (!stylistsByShop.TryGetValue(shopRow.Id, out var stylists) || stylists.Count == 0) continue;
            if (!servicesByShop.TryGetValue(shopRow.Id, out var services) || services.Count == 0) continue;

            for (var i = 0; i < 2; i++)
            {
                var customer = secondaryCustomers.Count == 0 ? null : secondaryCustomers[(dayCursor + i) % secondaryCustomers.Count];
                if (customer is null) continue;

                var stylist = stylists[i % stylists.Count];
                var service = services[Rng.Next(services.Count)];
                var pastDays = -(12 + 7 * i + (dayCursor % 20));
                var scheduledAtNear = now.AddHours(2 + i);

                var bookingResult = Booking.Initiate(
                    customer.Id, shopRow.Id, stylist.Id, scheduledAtNear,
                    new (Guid, string, int, int, long)[] { (service.Id, service.Name, service.DurationMinutes, service.BufferMinutes, service.PriceMinor) },
                    BookingType.Scheduled, CommissionRate, requiresOnlinePayment: false);
                if (bookingResult.IsFailure) continue;
                var booking = bookingResult.Value;

                var payment = CreateAndCapturePayment(booking, customer.Id, shopRow.Id);
                booking.MarkPaymentReceived(payment.Id);
                booking.OwnerConfirm();
                booking.Start();
                booking.Complete();
                var shift = RealisticSlotUtc(todayLocal.AddDays(pastDays), dayCursor + i * 11, service.DurationMinutes) - scheduledAtNear;
                BackdateBooking(booking, shift, confirmed: true, started: true, completed: true, cancelled: false);
                BackdatePayment(payment, shift);

                bookingsToAdd.Add(booking);
                paymentsToAdd.Add(payment);
                reviewCandidates.Add(booking);
            }

            // 1 extra upcoming Confirmed booking (secondary customer, different stylist index
            // than the review-pool loop's `i` values above so the EXCLUDE constraint on
            // overlapping [Pending,Confirmed,Started] slots per stylist can't collide).
            if (secondaryCustomers.Count > 0 && confirmedExtraCount < 13)
            {
                var extraCustomer = secondaryCustomers[(dayCursor + 5) % secondaryCustomers.Count];
                var extraStylist = stylists[^1];
                var extraService = services[0];
                var extraResult = Booking.Initiate(
                    extraCustomer.Id, shopRow.Id, extraStylist.Id,
                    RealisticSlotUtc(todayLocal.AddDays(3 + dayCursor % 10), dayCursor * 3 + 7, extraService.DurationMinutes),
                    new (Guid, string, int, int, long)[] { (extraService.Id, extraService.Name, extraService.DurationMinutes, extraService.BufferMinutes, extraService.PriceMinor) },
                    BookingType.Scheduled, CommissionRate, requiresOnlinePayment: false);
                if (extraResult.IsSuccess)
                {
                    var extraBooking = extraResult.Value;
                    extraBooking.OwnerConfirm();
                    bookingsToAdd.Add(extraBooking);
                    confirmedExtraCount++;
                }
            }

            dayCursor++;
        }

        db.Bookings.AddRange(bookingsToAdd);
        db.Payments.AddRange(paymentsToAdd);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("RichDemoSeeder: persisted {N} review-pool/extra bookings across {Shops} shops.", bookingsToAdd.Count, allErzurumShops.Count);

        // ── Step 6: reviews — 15-25 target, some owner-replied, 1-2 Pending moderation ──
        // İlk 2 aday DETERMİNİSTİK olarak Pending moderasyona zorlanır (profanity stopword
        // "rezalet") — geri kalanı olasılıksal seçim/puan/cevap alır. Yalnız Rng'ye bırakmak
        // (önceki sürüm) hem kırılgan hem de yanlıştı: bu sınıfın alanı static readonly Random
        // idi, aynı test process'inde ÖNCE çalışan başka bir test (ör. idempotency testi,
        // SeedAsync'i iki kez çağırıyor) paylaşılan Rng'nin durumunu tüketiyor ve xUnit'in test
        // sırası garantisiz olduğundan "Pending review var mı" testi ÇALIŞTIRMA SIRASINA göre
        // flaky oluyordu (adversarial kanıt: full suite koşumunda gerçekten yakalandı).
        var reviewsToAdd = new List<Review>();
        var ownersByShop = allErzurumShops.ToDictionary(s => s.Id, s => s.OwnerId);
        const int guaranteedPendingCount = 2;

        for (var idx = 0; idx < reviewCandidates.Count; idx++)
        {
            var booking = reviewCandidates[idx];
            var forcePending = idx < guaranteedPendingCount;
            if (!forcePending && Rng.NextDouble() > 0.75) continue;

            var rating = WeightedRating();
            string? body = forcePending
                ? "Rezalet bir deneyimdi, hiç memnun kalmadım." // profanity stopword ("rezalet") → Pending moderasyon
                : (Rng.NextDouble() < 0.6 ? PickReviewBody(rating) : null);

            var reviewResult = Review.Submit(booking.CustomerId, booking.BarberShopId, booking.StylistId, booking.Id, rating, body);
            if (reviewResult.IsFailure) continue;
            var review = reviewResult.Value;

            if (review.ModerationStatus == ReviewModerationStatus.Approved
                && ownersByShop.TryGetValue(booking.BarberShopId, out var ownerId)
                && Rng.NextDouble() < 0.30)
            {
                review.AddReply(ownerId, PickReplyText(rating));
            }

            reviewsToAdd.Add(review);
        }

        db.Reviews.AddRange(reviewsToAdd);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("RichDemoSeeder: created {N} reviews (target 15-25).", reviewsToAdd.Count);

        // ── Step 7: notifications — mixed read/unread for Demo Müşteri ──
        if (demoCustomer is not null)
        {
            var notifs = new List<NotificationLog>();

            var welcome = NotificationLog.Create(demoCustomer.Id, NotificationChannel.InApp, "system.welcome",
                demoCustomer.Id.ToString(), "Kuaforum'a hoş geldin", "Erzurum'daki en iyi salonları keşfet.");
            welcome.MarkSent(null);
            welcome.MarkRead();
            notifs.Add(welcome);

            var depositReminder = NotificationLog.Create(demoCustomer.Id, NotificationChannel.Push, "booking.deposit_paid",
                $"fcm-mock-{demoCustomer.Id:N}"[..20], "Kaporan alındı", "Randevun için kaporan alındı, kuaförün onayını bekliyor.");
            depositReminder.MarkSent($"mock-msg-{Guid.NewGuid():N}"[..16]);
            notifs.Add(depositReminder); // unread — bilinçli, "okunmamış" örneği bu

            var reviewRequest = NotificationLog.Create(demoCustomer.Id, NotificationChannel.Sms, "booking.review_request",
                demoCustomer.PhoneE164 ?? CustomerDemoPhone, "Yorumun bekleniyor", "Randevun tamamlandı, deneyimini paylaşır mısın?");
            reviewRequest.MarkSent($"netgsm-mock-{Guid.NewGuid():N}"[..16]);
            reviewRequest.MarkRead();
            notifs.Add(reviewRequest);

            var noShowNotice = NotificationLog.Create(demoCustomer.Id, NotificationChannel.Push, "booking.no_show_recorded",
                $"fcm-mock-{demoCustomer.Id:N}"[..20], "Randevuna gelmedin", "Geçen randevuna gelmediğin için kaydedildi.");
            noShowNotice.MarkSent($"mock-msg-{Guid.NewGuid():N}"[..16]);
            notifs.Add(noShowNotice); // unread

            var marketing = NotificationLog.Create(demoCustomer.Id, NotificationChannel.Email, "marketing.weekly_digest",
                "demo-musteri@example.com", "Haftalık öneriler", "Erzurum'da senin için seçtiğimiz salonlar.");
            marketing.MarkFailed("Mock SMTP timeout (demo)");
            notifs.Add(marketing);

            db.NotificationLogs.AddRange(notifs);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("RichDemoSeeder: created {N} notification logs for Demo Müşteri.", notifs.Count);
        }

        // ── Step 8: billing — 3 shops on a paid plan, Notified / Overdue→SuspensionApplied / Paid ──
        // RFC-014 §2 D1: LAUNCH_FREE (fiyat 0) faturalanmadığı için, faturalama ekranlarının
        // BOŞ KALMAMASI için bu 3 demo salonu ayrı bir demo/QA plana bağlanır — bkz.
        // DemoPaidPlanCode XML doc.
        if (newShops.Count >= 7)
        {
            await EnsureDemoPaidPlanAsync(ct);
            await BindShopToDemoPaidPlanAsync(newShops[1].Shop.Id, now, ct);
            await BindShopToDemoPaidPlanAsync(newShops[3].Shop.Id, now, ct);
            await BindShopToDemoPaidPlanAsync(newShops[6].Shop.Id, now, ct);
            await db.SaveChangesAsync(ct);

            var (periodStart, periodEnd) = LastMonthPeriod();

            // shop[1] (Zirve) — Notified, vadesi henüz gelmemiş.
            var notified = SubscriptionInvoice.Create(newShops[1].Shop.Id, periodStart, periodEnd, DemoPaidPlanCode, DemoPaidPlanPriceMinor).Value;
            notified.MarkNotified();
            db.SubscriptionInvoices.Add(notified);

            // shop[3] (Vadi) — Overdue → SuspensionApplied, shop de gerçekten askıya alınır
            // (SuspendOverdueSubscriptionsJob'un yaptığı gibi — fatura + shop birlikte).
            var overdue = SubscriptionInvoice.Create(newShops[3].Shop.Id, periodStart, periodEnd, DemoPaidPlanCode, DemoPaidPlanPriceMinor).Value;
            overdue.MarkNotified();
            overdue.MarkOverdue();
            overdue.ApplySuspension();
            db.SubscriptionInvoices.Add(overdue);
            var vadiTracked = await db.BarberShops.FirstAsync(s => s.Id == newShops[3].Shop.Id, ct);
            vadiTracked.Suspend("Ödenmemiş abonelik borcu — demo veri.");

            // shop[6] (Taze) — Paid.
            var paid = SubscriptionInvoice.Create(newShops[6].Shop.Id, periodStart, periodEnd, DemoPaidPlanCode, DemoPaidPlanPriceMinor).Value;
            paid.MarkNotified();
            paid.MarkPaid();
            db.SubscriptionInvoices.Add(paid);

            await db.SaveChangesAsync(ct);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE subscription_invoices SET due_date = {DateOnly.FromDateTime(now.UtcDateTime).AddDays(5)} WHERE id = {notified.Id}", ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE subscription_invoices SET due_date = {DateOnly.FromDateTime(now.UtcDateTime).AddDays(-3)} WHERE id = {overdue.Id}", ct);

            logger.LogInformation("RichDemoSeeder: created 3 subscription invoices (Notified/SuspensionApplied/Paid) on demo paid plan.");
        }

        // ── Step 9: dev-login "BarberOwner" personasının salonu ("Modern Stüdyo Kuaför") ──
        // Kusur 1 fix: bu şablona ait TEK bir randevu bile yoktu — dev-login ile giren founder
        // "Bugün boş", Toplam/Bekleyen/Ciro ₺0, "BU HAFTA ₺0" görüyordu. Owner'ın telefonu
        // hard-code EDİLMİYOR: DevLoginCommand'ın BARBEROwner aramasıyla birebir aynı yoldan
        // (telefon → User → BarberShop.OwnerId) bulunuyor.
        var devOwner = await db.Users.FirstOrDefaultAsync(u => u.PhoneE164 == DevLoginOwnerPhone, ct);
        var devShop = devOwner is null ? null : await db.BarberShops.FirstOrDefaultAsync(s => s.OwnerId == devOwner.Id, ct);

        if (devOwner is null || devShop is null)
        {
            logger.LogWarning("RichDemoSeeder: dev-login BarberOwner ({Phone}) veya salonu bulunamadı — Step 9 atlandı.", DevLoginOwnerPhone);
        }
        else
        {
            var devStylists = await db.Stylists.AsNoTracking()
                .Where(s => s.BarberShopId == devShop.Id && s.IsActive)
                .OrderBy(s => s.DisplayName)
                .ToListAsync(ct);
            var devServices = await db.ServiceCatalogs.AsNoTracking()
                .Where(s => s.BarberShopId == devShop.Id && s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync(ct);

            var devCustomerPool = new List<User>();
            if (demoCustomer is not null) devCustomerPool.Add(demoCustomer);
            devCustomerPool.AddRange(secondaryCustomers);

            if (devStylists.Count < 2 || devServices.Count == 0 || devCustomerPool.Count == 0)
            {
                logger.LogWarning(
                    "RichDemoSeeder: dev-login salonunda beklenen veri yok (stylists={S} services={V} customers={C}) — Step 9 atlandı.",
                    devStylists.Count, devServices.Count, devCustomerPool.Count);
            }
            else
            {
                var devBookings = new List<Booking>();
                var devPayments = new List<Payment>();
                var devReviewCandidates = new List<Booking>();
                var devCustIdx = 0;
                User NextDevCustomer()
                {
                    var c = devCustomerPool[devCustIdx % devCustomerPool.Count];
                    devCustIdx++;
                    return c;
                }

                Booking? MakeDevBooking(int stylistIdx, int serviceIdx, DateTimeOffset scheduledAt, bool requiresOnlinePayment = false, decimal? depositSnapshot = null)
                {
                    var stylist = devStylists[stylistIdx % devStylists.Count];
                    var service = devServices[serviceIdx % devServices.Count];
                    var customer = NextDevCustomer();
                    var result = Booking.Initiate(
                        customer.Id, devShop.Id, stylist.Id, scheduledAt,
                        new (Guid, string, int, int, long)[] { (service.Id, service.Name, service.DurationMinutes, service.BufferMinutes, service.PriceMinor) },
                        BookingType.Scheduled, CommissionRate, requiresOnlinePayment, depositPercentSnapshot: depositSnapshot);
                    return result.IsSuccess ? result.Value : null;
                }

                var placeholderFuture = now.AddHours(1); // Initiate()'in "geçmişte olamaz" guard'ını geçmek için — hepsi aşağıda backdate edilecek.

                // Kusur fix (2026-08-07, RichDemoSeederTests.SeedAsync_AtVariousLocalHours_...
                // regresyonu): "bugünün" VE gelecek 14 gün + bu hafta/geçen hafta geçmiş
                // verisinin TÜM randevu yerleşimleri artık STYLIST+GÜN başına PAYLAŞILAN tek bir
                // slot imleci (ReserveDevSlot) üzerinden geçer. Önceki sürüm her randevu grubunu
                // (Completed1/Completed2/Confirmed/Pending/kapora/geçmiş hafta/gelecek 14 gün)
                // BAĞIMSIZ slot hesaplarıyla yerleştiriyordu; aynı stiliste aynı gün çakışan
                // randevular sessizce üretiliyordu — çünkü ex_bookings_stylist_slot EXCLUDE
                // constraint'i (M15 migration) yalnız status IN (Pending, Confirmed, Started)
                // predicate'ine sahip, Completed KAPSAM DIŞI, yani DB bu hatayı hiç yakalamıyordu
                // (canlı örnek: aynı 155dk'lık hizmet iki Completed randevuya aynı anda atanmıştı).
                // ReserveDevSlot her çağrıda o (gün, stylist) ikilisi için "şu ana kadar dolu olan
                // son slot"un ÖTESİNDEN başlar ve hizmet süresi + tampon kadar ilerletir; iş
                // saatleri penceresine SIĞMAYAN bir randevu için null döner — çağıran taraf (mevcut
                // null-kontrolü kalıbıyla) o randevuyu üretmez: çakışan üretmektense eksik üretmek
                // yeğdir. windowMinutes/slotCount hesabı bilerek RealisticSlotUtc'nin kendi
                // formülüyle birebir aynıdır (bkz. orada) — aksi halde fit-check burada geçer ama
                // RealisticSlotUtc kendi slotCount'una göre modulo ile SESSİZCE farklı bir saate
                // "sarardı" (wrap), iki farklı süreli hizmet aynı slot indeksini FARKLI gerçek
                // saatlere haritalayabilirdi.
                var devStylistDaySlots = new Dictionary<(DateOnly Day, Guid StylistId), int>();
                int? ReserveDevSlot(DateOnly day, Guid stylistId, int minSlotIndex, int durationMinutes)
                {
                    var windowMinutes = (int)(DefaultCloseTime.ToTimeSpan() - DefaultOpenTime.ToTimeSpan()).TotalMinutes;
                    var key = (day, stylistId);
                    var start = Math.Max(Math.Max(minSlotIndex, 0), devStylistDaySlots.GetValueOrDefault(key, 0));
                    if (start * 15 + durationMinutes > windowMinutes) return null; // sığmıyor
                    devStylistDaySlots[key] = start + SlotsForService(durationMinutes);
                    return start;
                }

                // 1) BUGÜN — 2 Completed (gerçekten geçmişte, mümkün olduğunca iş saatleri içinde).
                var pastIdxToday = PastSlotIndexBeforeNow(now);
                var completed1Duration = devServices[0 % devServices.Count].DurationMinutes;
                var completed1Slot = ReserveDevSlot(todayLocal, devStylists[0 % devStylists.Count].Id, 0, completed1Duration);
                var todayCompleted1 = completed1Slot is null ? null : MakeDevBooking(0, 0, placeholderFuture);
                if (todayCompleted1 is not null)
                {
                    var payment = CreateAndCapturePayment(todayCompleted1, todayCompleted1.CustomerId, devShop.Id);
                    todayCompleted1.MarkPaymentReceived(payment.Id);
                    todayCompleted1.OwnerConfirm();
                    todayCompleted1.Start();
                    todayCompleted1.Complete();
                    payment.MarkSettled();
                    var target = RealisticSlotUtc(todayLocal, completed1Slot!.Value, completed1Duration);
                    var shift = target - todayCompleted1.ScheduledAt;
                    BackdateBooking(todayCompleted1, shift, confirmed: true, started: true, completed: true, cancelled: false);
                    BackdatePayment(payment, shift);
                    devBookings.Add(todayCompleted1);
                    devPayments.Add(payment);
                    devReviewCandidates.Add(todayCompleted1);
                }

                var completed2Duration = devServices[1 % devServices.Count].DurationMinutes;
                var completed2Slot = ReserveDevSlot(todayLocal, devStylists[1 % devStylists.Count].Id, Math.Min(pastIdxToday, 4), completed2Duration);
                var todayCompleted2 = completed2Slot is null ? null : MakeDevBooking(1, 1, placeholderFuture);
                if (todayCompleted2 is not null)
                {
                    var payment = CreateAndCapturePayment(todayCompleted2, todayCompleted2.CustomerId, devShop.Id);
                    todayCompleted2.MarkPaymentReceived(payment.Id);
                    todayCompleted2.OwnerConfirm();
                    todayCompleted2.Start();
                    todayCompleted2.Complete();
                    payment.MarkSettled();
                    var target = RealisticSlotUtc(todayLocal, completed2Slot!.Value, completed2Duration);
                    var shift = target - todayCompleted2.ScheduledAt;
                    BackdateBooking(todayCompleted2, shift, confirmed: true, started: true, completed: true, cancelled: false);
                    BackdatePayment(payment, shift);
                    devBookings.Add(todayCompleted2);
                    devPayments.Add(payment);
                    devReviewCandidates.Add(todayCompleted2);
                }

                // 2) BUGÜN — 2 Confirmed (ilerleyen saatler) + 1 Pending (onay bekliyor). Alt sınır
                // NextSlotIndexAfterNow(now) — "şimdiden X dk sonra"dan ÖNCE olamaz. ReserveDevSlot
                // bu alt sınırı, o stiliste BUGÜN ZATEN yerleştirilmiş randevuların (Completed1/2
                // dahil) kursörüyle otomatik birleştirir (max) — ayrı bir "OccupiedUntil" hesabına
                // artık gerek yok. En kısa 3 hizmet seçilir (uzun bir Boyama/Röfle 150dk'lık hizmet
                // günün geç saatinde seed edilince kapanış saatini rahatça aşabilirdi) — kalan
                // iş-saati penceresine sığma ihtimalini maksimize eder.
                var shortServiceIdx = devServices
                    .Select((s, idx) => (s.DurationMinutes, idx))
                    .OrderBy(x => x.DurationMinutes)
                    .Select(x => x.idx)
                    .ToList();
                var shortIdx0 = shortServiceIdx.Count > 0 ? shortServiceIdx[0] : 0;
                var shortIdx1 = shortServiceIdx.Count > 1 ? shortServiceIdx[1] : 0;
                var shortIdx2 = shortServiceIdx.Count > 2 ? shortServiceIdx[2] : shortIdx0;

                var nextSlot = NextSlotIndexAfterNow(now);

                var dur1 = devServices[shortIdx0].DurationMinutes;
                var confirmed1Slot = ReserveDevSlot(todayLocal, devStylists[0 % devStylists.Count].Id, nextSlot, dur1);
                var todayConfirmed1 = confirmed1Slot is null ? null : MakeDevBooking(0, shortIdx0, RealisticSlotUtc(todayLocal, confirmed1Slot.Value, dur1));
                if (todayConfirmed1 is not null)
                {
                    todayConfirmed1.OwnerConfirm();
                    devBookings.Add(todayConfirmed1);
                }

                var dur5 = devServices[shortIdx1].DurationMinutes;
                var confirmed2Slot = ReserveDevSlot(todayLocal, devStylists[1 % devStylists.Count].Id, nextSlot, dur5);
                var todayConfirmed2 = confirmed2Slot is null ? null : MakeDevBooking(1, shortIdx1, RealisticSlotUtc(todayLocal, confirmed2Slot.Value, dur5));
                if (todayConfirmed2 is not null)
                {
                    todayConfirmed2.OwnerConfirm();
                    devBookings.Add(todayConfirmed2);
                }

                var dur6 = devServices[shortIdx2].DurationMinutes;
                var pendingSlot = ReserveDevSlot(todayLocal, devStylists[0 % devStylists.Count].Id, nextSlot, dur6);
                var todayPending = pendingSlot is null ? null : MakeDevBooking(0, shortIdx2, RealisticSlotUtc(todayLocal, pendingSlot.Value, dur6));
                if (todayPending is not null) devBookings.Add(todayPending);

                // 3) Kapora ödenmiş Pending (+5 gün) — owner "ödenmiş, onay bekliyor" akışını
                // burada da görsün. Bunun için salonun kapora ayarı önce açılıyor.
                if (settings is not null)
                {
                    var devPolicy = BookingPolicy.Create(true, 0.15m, 2, null, null, settings.DepositPercentMin, settings.DepositPercentMax);
                    if (devPolicy.IsSuccess) devShop.SetBookingPolicy(devPolicy.Value);
                }
                var depositDay = todayLocal.AddDays(5);
                var depositDuration = devServices[3 % devServices.Count].DurationMinutes;
                var depositSlot = ReserveDevSlot(depositDay, devStylists[0 % devStylists.Count].Id, 12, depositDuration);
                var devDeposit = depositSlot is null ? null : MakeDevBooking(0, 3, RealisticSlotUtc(depositDay, depositSlot.Value, depositDuration), requiresOnlinePayment: true, depositSnapshot: 0.15m);
                if (devDeposit is not null)
                {
                    var depositMinor = Kuaforum.Domain.Common.PaymentAmounts.CalculateDepositMinor(devDeposit.TotalAmountMinor, 0.15m);
                    var payment = Payment.Initiate(
                        devDeposit.Id, devDeposit.CustomerId, devShop.Id, depositMinor, platformCommissionMinor: 0,
                        PaymentProviderName.Mock, PaymentKind.Deposit, idempotencyKey: $"rich-dev-deposit-{devDeposit.Id:N}").Value;
                    payment.Capture($"RICH-DEV-TX-{devDeposit.Id:N}"[..32], depositMinor * 18 / 1000);
                    devDeposit.MarkPaymentReceived(payment.Id); // Status Pending kalır — onay bekliyor
                    devBookings.Add(devDeposit);
                    devPayments.Add(payment);
                }

                // 4) GELECEK 14 gün — 8 karışık Confirmed/Pending (takvim sekmesi dolsun).
                var future = new (int DayOffset, int StylistIdx, int ServiceIdx, int SlotIdx, bool Confirm)[]
                {
                    (2, 0, 0, 10, true), (3, 1, 4, 14, false), (4, 0, 5, 22, true), (6, 1, 1, 8, false),
                    (8, 0, 4, 30, true), (9, 1, 5, 4, false), (11, 0, 0, 16, true), (13, 1, 2, 26, false),
                };
                foreach (var f in future)
                {
                    var duration = devServices[f.ServiceIdx % devServices.Count].DurationMinutes;
                    var day = todayLocal.AddDays(f.DayOffset);
                    var slot = ReserveDevSlot(day, devStylists[f.StylistIdx % devStylists.Count].Id, f.SlotIdx, duration);
                    if (slot is null) continue;
                    var slotAt = RealisticSlotUtc(day, slot.Value, duration);
                    var b = MakeDevBooking(f.StylistIdx, f.ServiceIdx, slotAt);
                    if (b is null) continue;
                    if (f.Confirm) b.OwnerConfirm();
                    devBookings.Add(b);
                }

                db.Bookings.AddRange(devBookings);
                db.Payments.AddRange(devPayments);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("RichDemoSeeder: dev-login owner şablonuna {N} randevu eklendi (bugün + bu hafta + gelecek 14 gün).", devBookings.Count);

                // 5) BU HAFTA (bugün dahil en az 6 Completed) + GEÇEN HAFTA (3-4 Completed) —
                // "BU HAFTA ₺X, N tamamlanan" ve geçen haftaya göre % karşılaştırması dolsun.
                // daysSinceMonday'a göre offset — hangi gün seed edilirse edilsin doğru UTC
                // hafta penceresine düşer (GetOwnerDashboardStatsQueryHandler.MondayStartUtc).
                var historyBookings = new List<Booking>();
                var historyPayments = new List<Payment>();
                const int extraThisWeekCompleted = 4; // + bugünün 2'si = en az 6
                for (var i = 0; i < extraThisWeekCompleted; i++)
                {
                    var dayOffset = daysSinceMonday == 0 ? 0 : -(1 + (i % daysSinceMonday));
                    AddDevHistoryCompleted(dayOffset, i);
                }
                const int lastWeekCompleted = 4;
                for (var i = 0; i < lastWeekCompleted; i++)
                {
                    var dayOffset = -(daysSinceMonday + 2 + i);
                    AddDevHistoryCompleted(dayOffset, extraThisWeekCompleted + i);
                }

                void AddDevHistoryCompleted(int dayOffset, int idx)
                {
                    var stylistIdx = idx % 2;
                    var serviceIdx = idx % devServices.Count;
                    var duration = devServices[serviceIdx].DurationMinutes;
                    var day = todayLocal.AddDays(dayOffset);
                    // dayOffset 0 (bugün — daysSinceMonday==0 iken, yani Pazartesi seed edilirse
                    // olur) BUGÜNÜN kendi kursörüyle (devStylistDaySlots) AYNI anahtarı bilerek
                    // paylaşır: aksi halde bu geçmiş kaydı todayCompleted1/2'nin/Confirmed'lerin
                    // ÜZERİNE sessizce yazılırdı (canlı örnek: idx=0 → stylist0/slot0, tam olarak
                    // todayCompleted1'in yerleştiği slot — bkz. ReserveDevSlot doc'u).
                    var slot = ReserveDevSlot(day, devStylists[stylistIdx % devStylists.Count].Id, 0, duration);
                    if (slot is null) return; // sığmıyor — bu geçmiş kaydı atlanır
                    var b = MakeDevBooking(stylistIdx, serviceIdx, now.AddHours(1));
                    if (b is null) return;
                    var payment = CreateAndCapturePayment(b, b.CustomerId, devShop.Id);
                    b.MarkPaymentReceived(payment.Id);
                    b.OwnerConfirm();
                    b.Start();
                    b.Complete();
                    payment.MarkSettled();
                    var target = RealisticSlotUtc(day, slot.Value, duration);
                    var shift = target - b.ScheduledAt;
                    BackdateBooking(b, shift, confirmed: true, started: true, completed: true, cancelled: false);
                    BackdatePayment(payment, shift);
                    historyBookings.Add(b);
                    historyPayments.Add(payment);
                    devReviewCandidates.Add(b);
                }

                db.Bookings.AddRange(historyBookings);
                db.Payments.AddRange(historyPayments);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("RichDemoSeeder: dev-login owner şablonuna {N} geçmiş (bu hafta/geçen hafta) Completed randevu eklendi.", historyBookings.Count);

                // 6) Faturalama — en az 1 Notified + 1 Paid (owner faturalama ekranı boş kalmasın).
                // RFC-014 §2 D1: LAUNCH_FREE faturalanmadığı için devShop da demo/QA plana bağlanır.
                await EnsureDemoPaidPlanAsync(ct);
                await BindShopToDemoPaidPlanAsync(devShop.Id, now, ct);
                await db.SaveChangesAsync(ct);

                var (devPeriodStart, devPeriodEnd) = LastMonthPeriod();
                var devNotified = SubscriptionInvoice.Create(devShop.Id, devPeriodStart, devPeriodEnd, DemoPaidPlanCode, DemoPaidPlanPriceMinor).Value;
                devNotified.MarkNotified();
                db.SubscriptionInvoices.Add(devNotified);

                var priorPeriodStart = devPeriodStart.AddMonths(-1);
                var priorPeriodEnd = devPeriodStart.AddDays(-1);
                var devPaid = SubscriptionInvoice.Create(devShop.Id, priorPeriodStart, priorPeriodEnd, DemoPaidPlanCode, DemoPaidPlanPriceMinor).Value;
                devPaid.MarkNotified();
                devPaid.MarkPaid();
                db.SubscriptionInvoices.Add(devPaid);
                await db.SaveChangesAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE subscription_invoices SET due_date = {DateOnly.FromDateTime(now.UtcDateTime).AddDays(10)} WHERE id = {devNotified.Id}", ct);
                logger.LogInformation("RichDemoSeeder: dev-login owner şablonuna 2 abonelik faturası eklendi (Notified/Paid).");

                // 7) Bildirimler — yeni randevu talebi/kapora/iptal/faturalama, okunmuş/okunmamış karışık.
                var devNotifs = new List<NotificationLog>();
                var newRequest = NotificationLog.Create(devOwner.Id, NotificationChannel.Push, "booking.new_request",
                    $"fcm-mock-{devOwner.Id:N}"[..20], "Yeni randevu talebi", "Bugün için yeni bir randevu talebi var, onayını bekliyor.");
                newRequest.MarkSent($"mock-msg-{Guid.NewGuid():N}"[..16]);
                devNotifs.Add(newRequest); // unread

                var depositNotice = NotificationLog.Create(devOwner.Id, NotificationChannel.Push, "booking.deposit_paid",
                    $"fcm-mock-{devOwner.Id:N}"[..20], "Kapora alındı", "Bir randevu için kapora ödendi, onayını bekliyor.");
                depositNotice.MarkSent($"mock-msg-{Guid.NewGuid():N}"[..16]);
                depositNotice.MarkRead();
                devNotifs.Add(depositNotice);

                var cancelNotice = NotificationLog.Create(devOwner.Id, NotificationChannel.InApp, "booking.cancelled",
                    devOwner.Id.ToString(), "Randevu iptal edildi", "Bir müşteri randevusunu iptal etti.");
                cancelNotice.MarkSent(null);
                cancelNotice.MarkRead();
                devNotifs.Add(cancelNotice);

                var billingNotice = NotificationLog.Create(devOwner.Id, NotificationChannel.Sms, "billing.commission_notified",
                    devOwner.PhoneE164 ?? DevLoginOwnerPhone, "Komisyon faturan hazır", "Bu ay için komisyon faturan oluşturuldu.");
                billingNotice.MarkSent($"netgsm-mock-{Guid.NewGuid():N}"[..16]);
                devNotifs.Add(billingNotice); // unread

                db.NotificationLogs.AddRange(devNotifs);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("RichDemoSeeder: dev-login owner'a {N} bildirim eklendi.", devNotifs.Count);

                // 8) Yorumlar — biri owner cevaplı, ortalama puan oluşsun.
                var devReviews = new List<Review>();
                var devRatings = new[] { 5, 4, 5, 3, 4 };
                for (var i = 0; i < devReviewCandidates.Count && i < devRatings.Length; i++)
                {
                    var booking = devReviewCandidates[i];
                    var rating = devRatings[i];
                    var body = PickReviewBody(rating);
                    var reviewResult = Review.Submit(booking.CustomerId, booking.BarberShopId, booking.StylistId, booking.Id, rating, body);
                    if (reviewResult.IsFailure) continue;
                    var review = reviewResult.Value;
                    if (i == 0 && review.ModerationStatus == ReviewModerationStatus.Approved)
                        review.AddReply(devOwner.Id, PickReplyText(rating));
                    devReviews.Add(review);
                }
                db.Reviews.AddRange(devReviews);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("RichDemoSeeder: dev-login owner şablonuna {N} yorum eklendi.", devReviews.Count);
            }
        }

        // ── Step 10: admin KYC kuyruğu — kuyruk şu ana kadar HER ZAMAN boştu, çünkü
        // DemoBarberSeeder/RichDemoSeeder şablonları doğrudan BarberShop.ApproveKyc() ile
        // Active'e geçiyordu — hiçbir zaman gerçek bir KycSubmission satırı üretilmiyordu (admin
        // kuyruğu KycSubmission tablosunu okur, BarberShopStatus'ü DEĞİL). 2 yeni Draft salon:
        // biri Pending (yeni başvuru), biri UnderReview (admin incelemeye almış) — admin onay/red
        // akışı elle test edilebilsin. Dev-login "BarberOwnerDraft" personasının kendi şablonuna
        // (Şişli Draft, KYC akışının TAZE kalması gereken tek yer) BİLEREK dokunulmuyor.
        var kycIstanbul = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.PlateCode == 34, ct);
        var kycDistrict = kycIstanbul is null ? null : await db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.CityId == kycIstanbul.Id, ct);
        var adminUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Admin, ct);

        if (kycIstanbul is null || kycDistrict is null)
        {
            logger.LogWarning("RichDemoSeeder: İstanbul/ilçe bulunamadı — Step 10 (KYC kuyruğu) atlandı.");
        }
        else
        {
            var kycShopSpecs = new (string OwnerPhone, string OwnerName, string ShopName, KycStatus TargetStatus)[]
            {
                ("+905003000001", "Kadir Bekleyen", "Bekleyen Kuaför Stüdyosu", KycStatus.Pending),
                ("+905003000002", "Zehra İnceleme", "İncelemede Güzellik Salonu", KycStatus.UnderReview),
            };
            var kycCreated = 0;

            foreach (var spec in kycShopSpecs)
            {
                var exists = await db.Users.AnyAsync(u => u.PhoneE164 == spec.OwnerPhone, ct);
                if (exists) continue; // idempotency: bu seeder'ın üst-düzey gate'i zaten var ama savunmacı

                var phoneResult = PhoneE164.TryCreate(spec.OwnerPhone);
                if (phoneResult.IsFailure) continue;
                var ownerResult = User.CreateCustomerByPhone(phoneResult.Value, spec.OwnerName);
                if (ownerResult.IsFailure) continue;
                var kycOwner = ownerResult.Value;
                kycOwner.PromoteToBarberOwner();
                db.Users.Add(kycOwner);

                var shopResult = BarberShop.Create(kycOwner.Id, spec.ShopName, SalonGenderType.Unisex);
                if (shopResult.IsFailure) continue;
                var kycShop = shopResult.Value;
                kycShop.UpdateBasics(name: spec.ShopName, description: "KYC test — admin onay/red akışı için demo başvuru.", genderType: null, phone: null);

                var coordResult = Coordinate.TryCreate(41.0, 29.0);
                if (coordResult.IsSuccess)
                {
                    var addressResult = Address.Create(kycIstanbul.Id, kycDistrict.Id, null, "Demo Cad. No:1", coordResult.Value);
                    if (addressResult.IsSuccess) kycShop.SetAddress(addressResult.Value);
                }

                var markPending = kycShop.MarkPendingKyc();
                if (markPending.IsFailure) continue;
                db.BarberShops.Add(kycShop);

                var submission = KycSubmission.Create(kycShop.Id, kycOwner.Id);
                db.KycSubmissions.Add(submission);

                var taxDoc = KycDocument.Create(submission.Id, DocumentType.TaxLicense, "vergi-levhasi.pdf", $"kyc/demo/{submission.Id:N}/tax.pdf", 128_000, "application/pdf");
                if (taxDoc.IsSuccess) db.KycDocuments.Add(taxDoc.Value);
                var idDoc = KycDocument.Create(submission.Id, DocumentType.IdentityCard, "kimlik.jpg", $"kyc/demo/{submission.Id:N}/id.jpg", 96_000, "image/jpeg");
                if (idDoc.IsSuccess) db.KycDocuments.Add(idDoc.Value);

                if (spec.TargetStatus == KycStatus.UnderReview)
                {
                    if (adminUser is not null)
                        submission.MarkUnderReview(adminUser.Id);
                    else
                        logger.LogWarning("RichDemoSeeder: admin kullanıcısı yok — {Shop} başvurusu UnderReview yerine Pending kalacak.", spec.ShopName);
                }

                kycCreated++;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("RichDemoSeeder: admin KYC kuyruğuna {N} örnek başvuru eklendi (Pending/UnderReview).", kycCreated);
        }

        logger.LogInformation("RichDemoSeeder: complete. newShops={NewShops} totalErzurumShops={Total} reviews={Reviews}",
            newShops.Count, allErzurumShops.Count, reviewsToAdd.Count);
    }

    /// <summary>DemoBarberSeeder/CommissionBillingTests'in ortak kalıbı — dönem her zaman bir önceki takvim ayı.</summary>
    private static (DateOnly PeriodStart, DateOnly PeriodEnd) LastMonthPeriod()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>Idempotent — DemoPaidPlanCode satırı + entitlement'ları yoksa oluşturur. Bkz. DemoPaidPlanCode XML doc.</summary>
    private async Task EnsureDemoPaidPlanAsync(CancellationToken ct)
    {
        if (await db.Set<SubscriptionPlan>().AnyAsync(p => p.Code == DemoPaidPlanCode, ct)) return;

        var plan = SubscriptionPlan.Create(DemoPaidPlanCode, "Demo QA Paket", DemoPaidPlanPriceMinor, isActive: false, displayOrder: 99).Value;
        db.Set<SubscriptionPlan>().Add(plan);

        foreach (var (key, value) in new[]
        {
            (EntitlementKeys.MaxStaff, "10"),
            (EntitlementKeys.MaxMonthlyBookings, "-1"),
            (EntitlementKeys.CanUseSms, "true"),
            (EntitlementKeys.CanUseDeposit, "true"),
        })
        {
            db.Set<PlanEntitlement>().Add(PlanEntitlement.Create(DemoPaidPlanCode, key, value).Value);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Salonu DemoPaidPlanCode'a bağlar — SubscriptionPlanSeeder'ın LAUNCH_FREE backfill'i
    /// yalnız ABONELİKSİZ salonları hedeflediği için (bkz. o seeder'ın XML doc'u), burada
    /// EnsureDemoPaidPlanAsync + bu metod SubscriptionPlanSeeder'dan ÖNCE (RichDemoSeeder
    /// içinde) çalışırsa backfill bu salonlara hiç dokunmaz.
    /// </summary>
    private async Task BindShopToDemoPaidPlanAsync(Guid shopId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Set<ShopSubscription>().FirstOrDefaultAsync(s => s.BarberShopId == shopId, ct);
        if (existing is null)
        {
            db.Set<ShopSubscription>().Add(ShopSubscription.Create(shopId, DemoPaidPlanCode, now.AddMonths(-1), now.AddMonths(11)).Value);
        }
        else if (existing.PlanCode != DemoPaidPlanCode)
        {
            existing.ChangePlan(DemoPaidPlanCode);
        }
    }

    /// <summary>
    /// Kusur 2 fix (2026-08-06, elle test bulgusu): önceki sürüm bir "now.AddDays(N)" hedefiyle
    /// "now.AddHours(X)" placeholder'ı arasındaki farkı (shift) hesaplayıp TÜM alanlara
    /// uyguluyordu — matematik saat bileşenini iptal ediyordu ve nihai ScheduledAt SUNUCUNUN O
    /// ANKİ SAAT-DAKİKASINDA sabitleniyordu ("14 Ağu Cum 06:58" gibi, kuaförler sabaha karşı
    /// randevu vermez). Bu yardımcı yalnızca GERÇEKÇİ bir iş-saati üretir: verilen yerel tarihte,
    /// çalışma saatleri içinde (yoksa 09:00–19:00 varsayımı), 15 dakikanın katı bir başlangıç —
    /// hizmet süresi kapanış saatini aşmayacak şekilde slot sayısı daraltılır. Random KULLANMAZ
    /// (bkz. Rng alanının doc'u — paylaşılan Random test sırasına göre flaky çıktı üretiyordu);
    /// yalnızca (localDate, slotIndex) girdisinden deterministik üretir — slotIndex slot sayısına
    /// modulo alınır, bu yüzden HERHANGİ bir int güvenlidir (aralık dışı çökme riski yok).
    /// Europe/Istanbul yerel saatine göre hesaplanır, DB'ye UTC olarak döner.
    /// </summary>
    private static DateTimeOffset RealisticSlotUtc(
        DateOnly localDate, int slotIndex, int serviceDurationMinutes = 45,
        TimeOnly? openTime = null, TimeOnly? closeTime = null)
    {
        var open = openTime ?? DefaultOpenTime;
        var close = closeTime ?? DefaultCloseTime;
        if (close <= open) { open = DefaultOpenTime; close = DefaultCloseTime; }

        var windowMinutes = (close.ToTimeSpan() - open.ToTimeSpan()).TotalMinutes;
        var reserve = Math.Clamp(serviceDurationMinutes, 15, (int)windowMinutes);
        var usableMinutes = Math.Max(15, windowMinutes - reserve);
        var slotCount = Math.Max(1, (int)(usableMinutes / 15) + 1);

        var offset = ((slotIndex % slotCount) + slotCount) % slotCount;
        var localStart = open.AddMinutes(offset * 15);

        var localDateTime = new DateTime(localDate.Year, localDate.Month, localDate.Day, localStart.Hour, localStart.Minute, 0, DateTimeKind.Unspecified);
        var utcOffset = IstanbulTz.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, utcOffset).ToUniversalTime();
    }

    /// <summary>
    /// "Bugün, gerçekten geçmişte kalmış" bir Completed randevu üretmek için: şu ana kadar
    /// açılıştan (09:00 varsayılan) geçen 15dk'lık slot sayısı, en az 1 slot pay bırakarak.
    /// Seeding çok erken saatte (ör. 03:00) çalışırsa 0 döner — o nadir kenar durumda çağıran
    /// taraf en erken slotu (index 0) kullanır, sonuç teknik olarak "gelecekte" kalabilir; bu
    /// bilinen ve kabul edilen bir sınır durumdur (raporda not düşülür).
    /// </summary>
    private static int PastSlotIndexBeforeNow(DateTimeOffset nowUtc, TimeOnly? openTime = null)
    {
        var open = openTime ?? DefaultOpenTime;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, IstanbulTz);
        var minutesSinceOpen = (nowLocal.TimeOfDay - open.ToTimeSpan()).TotalMinutes;
        if (minutesSinceOpen < 15) return 0;
        return Math.Max(0, (int)(minutesSinceOpen / 15) - 1);
    }

    /// <summary>
    /// Bugünün İLERLEYEN saatleri için ilk uygun 15dk slot indeksi (açılışa göre).
    ///
    /// Kusur (2026-08-07, suite sabah 07:39'da koşarken yakalandı): önceki sürüm bugünün
    /// Confirmed/Pending randevularını `now.AddMinutes(20)` şeklinde bir SAAT imleciyle
    /// yerleştiriyordu ve bu imleç iş saatlerine HİÇ kenetlenmiyordu. Mesai başlamadan
    /// (ör. 07:39) seed edilince randevu 08:39'a düşüyordu — kuaför henüz açık değil.
    /// Simetrik olarak akşam kapanıştan sonra seed edilince de kapanışı aşıyordu. Yani
    /// üretilen demo veri yalnız "gün ortasında seed edilirse" doğruydu.
    ///
    /// Çözüm: imleç artık saat değil SLOT İNDEKSİ; yerleştirmeyi zaten iş saatlerini garanti
    /// eden <see cref="RealisticSlotUtc"/> yapıyor. Mesai öncesi koşumda 0 (açılış) döner.
    /// Kapanışa çok yakın koşumda RealisticSlotUtc modulo ile gün içine kıvırır; oluşan slot
    /// geçmişte kalırsa <c>Booking.Initiate</c> guard'ı randevuyu üretmez ve çağıran taraf
    /// null kontrolüyle sessizce atlar — akşam seed'inde "Bugün" panelinin daha boş olması,
    /// iş saati dışında geçersiz veri üretmeye yeğlenir.
    /// </summary>
    private static int NextSlotIndexAfterNow(DateTimeOffset nowUtc, int leadMinutes = 20, TimeOnly? openTime = null)
    {
        var open = openTime ?? DefaultOpenTime;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, IstanbulTz).AddMinutes(leadMinutes);
        var minutesSinceOpen = (nowLocal.TimeOfDay - open.ToTimeSpan()).TotalMinutes;
        if (minutesSinceOpen <= 0) return 0;
        return (int)Math.Ceiling(minutesSinceOpen / 15);
    }

    /// <summary>Bir hizmet süresini (+15dk tampon) kaç 15dk'lık slot ilerleteceğini verir.</summary>
    private static int SlotsForService(int durationMinutes) => (int)Math.Ceiling((durationMinutes + 15) / 15.0);

    private Payment CreateAndCapturePayment(Booking booking, Guid customerId, Guid shopId)
    {
        var payment = Payment.Initiate(
            booking.Id, customerId, shopId, booking.TotalAmountMinor, platformCommissionMinor: 0,
            PaymentProviderName.Mock, PaymentKind.FullOnline, idempotencyKey: $"rich-{booking.Id:N}").Value;
        var pspMdr = booking.TotalAmountMinor * 18 / 1000;
        var txId = $"RICH-TX-{booking.Id:N}";
        payment.Capture(txId.Length > 32 ? txId[..32] : txId, pspMdr);
        return payment;
    }

    private static void BackdateBooking(Booking booking, TimeSpan shift, bool confirmed, bool started, bool completed, bool cancelled)
    {
        ShiftField(booking, "ScheduledAt", shift);
        ShiftField(booking, "ScheduledEndsAt", shift);
        ShiftField(booking, "CreatedAt", shift);
        ShiftField(booking, "UpdatedAt", shift);
        if (confirmed) { ShiftField(booking, "ConfirmedAt", shift); ShiftField(booking, "DepositPaidAt", shift); }
        if (started) ShiftField(booking, "StartedAt", shift);
        if (completed) ShiftField(booking, "CompletedAt", shift);
        if (cancelled) ShiftField(booking, "CancelledAt", shift);
    }

    private static void BackdatePayment(Payment payment, TimeSpan shift)
    {
        ShiftField(payment, "CreatedAt", shift);
        ShiftField(payment, "UpdatedAt", shift);
        ShiftField(payment, "AuthorizedAt", shift);
        ShiftField(payment, "CapturedAt", shift);
        ShiftField(payment, "SettledAt", shift);
    }

    private static void ShiftField(object entity, string propName, TimeSpan shift)
    {
        var field = FindBackingField(entity.GetType(), propName);
        if (field is null) return;
        var current = field.GetValue(entity);
        switch (current)
        {
            case DateTimeOffset dto:
                field.SetValue(entity, dto + shift);
                break;
            case null:
                break;
            default:
                if (field.FieldType == typeof(DateTimeOffset?))
                {
                    var nullable = (DateTimeOffset?)current;
                    if (nullable.HasValue) field.SetValue(entity, (DateTimeOffset?)(nullable.Value + shift));
                }
                break;
        }
    }

    private static FieldInfo? FindBackingField(Type type, string propName)
    {
        var current = type;
        while (current is not null && current != typeof(object))
        {
            var field = current.GetField($"<{propName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null) return field;
            current = current.BaseType;
        }
        return null;
    }

    private async Task GrantTosConsentIfMissingAsync(User? user, CancellationToken ct)
    {
        if (user is null) return;
        var hasConsent = await db.Consents.AnyAsync(c =>
            c.UserId == user.Id && c.Purpose == ConsentPurpose.TermsOfService && c.Granted
            && c.RevokedAt == null && c.Version == LegalDocuments.CurrentTosVersion, ct);
        if (hasConsent) return;
        db.Consents.Add(Consent.Grant(user.Id, ConsentPurpose.TermsOfService, LegalDocuments.CurrentTosVersion, ConsentSource.Registration));
    }

    private int WeightedRating()
    {
        var roll = Rng.NextDouble();
        return roll switch { < 0.55 => 5, < 0.80 => 4, < 0.92 => 3, < 0.97 => 2, _ => 1 };
    }

    private string PickReviewBody(int rating) => rating switch
    {
        5 => Sample(new[]
        {
            "Süper memnun kaldım, kesinlikle tavsiye ederim.",
            "Salon temiz, ilgi harika, kesin yine geleceğim.",
            "Tam istediğim gibi oldu, fiyat da uygun.",
        }),
        4 => Sample(new[]
        {
            "Genel olarak güzeldi, yine gelirim.",
            "Kesim güzel ama biraz beklemem gerekti.",
        }),
        3 => Sample(new[]
        {
            "Beklediğim kadar değildi, vasat sayılır.",
            "Fön kısmı biraz eksik kaldı.",
        }),
        2 => Sample(new[]
        {
            "Beklediğim gibi çıkmadı.",
            "Biraz uzun sürdü, aceleye geldi.",
        }),
        1 => Sample(new[]
        {
            "Hayal kırıklığıydı.",
        }),
        _ => "Yorum",
    };

    private string PickReplyText(int rating) => rating >= 4
        ? Sample(new[] { "Teşekkürler, yine bekleriz!", "Memnuniyetinize sevindik." })
        : Sample(new[] { "Geri bildiriminiz için teşekkürler, daha iyisini sunacağız." });

    private string Sample(string[] options) => options[Rng.Next(options.Length)];

    // ============================================================
    //                        STATIC DEMO DATA
    // ============================================================

    private record RichCustomer(string Phone, string DisplayName);

    private static readonly RichCustomer[] SecondaryCustomers =
    {
        new("+905004000001", "Ferhat Kutlu"),
        new("+905004000002", "Nurcan Şimşek"),
        new("+905004000003", "Cemil Aydemir"),
        new("+905004000004", "Gülcan Ertürk"),
        new("+905004000005", "Salih Baydar"),
        new("+905004000006", "Rukiye Tan"),
        new("+905004000007", "Kadir Özgür"),
        new("+905004000008", "Sema Yalman"),
    };

    private record NewShop(
        string DistrictName,
        string Slug,
        string ShopName,
        string Description,
        string ShopPhone,
        SalonGenderType GenderType,
        string AddressLine1,
        double Latitude,
        double Longitude,
        string OwnerName,
        string OwnerPhone,
        string[] Stylists,
        (string Name, ServiceGender Gender, int Duration, int PriceTl)[] Services);

    private static readonly NewShop[] NewErzurumShops =
    {
        // [0] — 10% kapora
        new(
            DistrictName: "Yakutiye", Slug: "yakutiye-lale-kadin",
            ShopName: "Lale Kadın Kuaförü", Description: "Yakutiye'de modern kadın saç tasarım stüdyosu.",
            ShopPhone: "+904421120001", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Lalezar Sok. No:12", Latitude: 39.9070, Longitude: 41.2690,
            OwnerName: "Lale Aydın", OwnerPhone: "+905002000001",
            Stylists: new[] { "Lale Aydın", "Sibel Kara", "Derya Tunç" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 45, 300),
                ("Saç Boyama", ServiceGender.Female, 90, 1000),
                ("Röfle", ServiceGender.Female, 150, 2000),
                ("Fön", ServiceGender.Female, 30, 200),
                ("Manikür", ServiceGender.Female, 30, 180),
                ("Kaş Laminasyon", ServiceGender.Female, 30, 250),
            }),

        // [1] — 20% kapora
        new(
            DistrictName: "Palandöken", Slug: "palandoken-zirve-unisex",
            ShopName: "Zirve Unisex Salon", Description: "Palandöken kayak merkezine yakın unisex salon.",
            ShopPhone: "+904422220007", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Kayak Merkezi Yolu No:3", Latitude: 39.8765, Longitude: 41.2955,
            OwnerName: "Serkan Zirve", OwnerPhone: "+905002000002",
            Stylists: new[] { "Serkan Zirve", "Nalan Yüksel" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 25, 200),
                ("Kadın Saç Kesim", ServiceGender.Female, 40, 320),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 100),
                ("Saç Boyama", ServiceGender.Female, 90, 900),
                ("Fön", ServiceGender.Female, 25, 180),
            }),

        // [2] — anti-spam (MinDaysBetweenBookings=7, MaxBookingsPerMonth=2)
        new(
            DistrictName: "Aziziye", Slug: "aziziye-huzur-erkek",
            ShopName: "Huzur Berber", Description: "Aziziye'de sakin, randevulu çalışan aile berberi.",
            ShopPhone: "+904423330009", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Huzur Mah. No:21", Latitude: 39.9488, Longitude: 41.1390,
            OwnerName: "Halil Huzur", OwnerPhone: "+905002000003",
            Stylists: new[] { "Halil Huzur", "Metin Sakin", "Cavit Yavaş", "Nihat Berber" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 160),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 90),
                ("Klasik Tıraş", ServiceGender.Male, 30, 210),
                ("Saç + Sakal", ServiceGender.Male, 35, 230),
                ("Yüz Bakımı", ServiceGender.Male, 30, 200),
                ("Yıkama", ServiceGender.Male, 10, 50),
            }),

        // [3] — plain
        new(
            DistrictName: "Pasinler", Slug: "pasinler-vadi-unisex",
            ShopName: "Vadi Unisex Salon", Description: "Pasinler ilçe merkezinde uygun fiyatlı salon.",
            ShopPhone: "+904426660001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Vadi Cad. No:8", Latitude: 39.9835, Longitude: 41.6772,
            OwnerName: "Emrah Vadi", OwnerPhone: "+905002000004",
            Stylists: new[] { "Emrah Vadi", "Songül Pasinler" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 20, 140),
                ("Kadın Saç Kesim", ServiceGender.Female, 35, 220),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 90),
                ("Renklendirme", ServiceGender.Female, 90, 850),
            }),

        // [4] — plain
        new(
            DistrictName: "Horasan", Slug: "horasan-yildiz-kadin",
            ShopName: "Yıldız Kadın Kuaförü", Description: "Horasan'da köklü, geniş hizmet yelpazeli kadın kuaförü.",
            ShopPhone: "+904427770001", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Yıldız Sok. No:4", Latitude: 40.0392, Longitude: 42.1719,
            OwnerName: "Yıldız Horasan", OwnerPhone: "+905002000005",
            Stylists: new[] { "Yıldız Horasan", "Gamze Yıldız", "Ceren Ak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 270),
                ("Saç Boyama", ServiceGender.Female, 90, 850),
                ("Manikür", ServiceGender.Female, 30, 170),
                ("Pedikür", ServiceGender.Female, 45, 220),
                ("Fön", ServiceGender.Female, 25, 180),
                ("Kalıcı Oje", ServiceGender.Female, 45, 300),
                ("Cilt Bakımı", ServiceGender.Female, 60, 650),
            }),

        // [5] — plain
        new(
            DistrictName: "Aşkale", Slug: "askale-dag-erkek",
            ShopName: "Dağ Erkek Kuaförü", Description: "Aşkale'de dağ havası kadar ferah bir erkek kuaförü.",
            ShopPhone: "+904428880001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Dağ Yolu No:15", Latitude: 39.9134, Longitude: 40.6994,
            OwnerName: "Fikret Dağ", OwnerPhone: "+905002000006",
            Stylists: new[] { "Fikret Dağ", "Bekir Aşkale" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 20, 140),
                ("Sakal Tıraşı", ServiceGender.Male, 12, 80),
                ("Saç + Sakal", ServiceGender.Male, 30, 200),
                ("Ustura Tıraş", ServiceGender.Male, 30, 220),
            }),

        // [6] — plain
        new(
            DistrictName: "Oltu", Slug: "oltu-taze-unisex",
            ShopName: "Taze Unisex Kuaför", Description: "Oltu'da genç ekip, taze trendler.",
            ShopPhone: "+904429990001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Taşlıca Mah. No:6", Latitude: 40.5449, Longitude: 41.9970,
            OwnerName: "Serap Oltu", OwnerPhone: "+905002000007",
            Stylists: new[] { "Serap Oltu", "Kaan Taze", "Aylin Genç", "Barış Delikanlı" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 20, 130),
                ("Kadın Saç Kesim", ServiceGender.Female, 30, 190),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 90),
                ("Fön", ServiceGender.Female, 25, 160),
                ("Saç Boyama", ServiceGender.Female, 80, 750),
                ("Manikür", ServiceGender.Female, 30, 150),
                ("Renklendirme", ServiceGender.Female, 90, 900),
                ("Kaş Şekillendirme", ServiceGender.Female, 15, 120),
            }),
    };
}
