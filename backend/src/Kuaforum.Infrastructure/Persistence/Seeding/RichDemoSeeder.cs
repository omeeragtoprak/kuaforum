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
    private const decimal CommissionRate = 0.10m;

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

    public async Task SeedAsync(CancellationToken ct = default)
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

        var now = DateTimeOffset.UtcNow;
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
            var depositBooking = MakeDemoBooking(0, now.AddDays(5).AddHours(11), requiresOnlinePayment: true, depositSnapshot: 0.10m);
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
                var shift = now.AddDays(-25) - completedBooking.ScheduledAt;
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
                var shift = now.AddDays(-10) - now.AddHours(3);
                BackdateBooking(cancelledBooking, shift, confirmed: false, started: false, completed: false, cancelled: true);
                bookingsToAdd.Add(cancelledBooking);
            }

            // 4) NoShow (past ~15 gün önce) — Confirmed sonrası raw-SQL status flip (mevcut test
            // suite'inin de kullandığı kalıp, MarkNoShow'un canlı "now" guard'ını atlamak için).
            var noShowBooking = MakeDemoBooking(5, now.AddHours(4));
            if (noShowBooking is not null)
            {
                noShowBooking.OwnerConfirm();
                var shift = now.AddDays(-15) - now.AddHours(4);
                BackdateBooking(noShowBooking, shift, confirmed: true, started: false, completed: false, cancelled: false);
                bookingsToAdd.Add(noShowBooking);
            }

            // 5) Confirmed (upcoming ~+4 gün)
            var confirmedBooking = MakeDemoBooking(6, now.AddDays(4).AddHours(13));
            if (confirmedBooking is not null)
            {
                confirmedBooking.OwnerConfirm();
                bookingsToAdd.Add(confirmedBooking);
            }

            // 6) Pending (upcoming ~+7 gün, owner onayı bekliyor, ödeme yok)
            var pendingBooking = MakeDemoBooking(2, now.AddDays(7).AddHours(15));
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
                var shift = now.AddDays(pastDays) - scheduledAtNear;
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
                    extraCustomer.Id, shopRow.Id, extraStylist.Id, now.AddDays(3 + dayCursor % 10).AddHours(16),
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

        // ── Step 8: billing — 3 shops, Notified / Overdue→SuspensionApplied / Paid ──
        if (newShops.Count >= 7)
        {
            var (periodStart, periodEnd) = LastMonthPeriod();
            var rate = settings?.CommissionRateGlobal ?? CommissionRate;

            // shop[1] (Zirve) — Notified, vadesi henüz gelmemiş.
            var notified = CommissionStatement.Create(newShops[1].Shop.Id, periodStart, periodEnd, 850_000, rate).Value;
            notified.MarkNotified();
            db.CommissionStatements.Add(notified);

            // shop[3] (Vadi) — Overdue → SuspensionApplied, shop de gerçekten askıya alınır
            // (SuspendOverdueCommissionAccountsJob'un yaptığı gibi — statement + shop birlikte).
            var overdue = CommissionStatement.Create(newShops[3].Shop.Id, periodStart, periodEnd, 620_000, rate).Value;
            overdue.MarkNotified();
            overdue.MarkOverdue();
            overdue.ApplySuspension();
            db.CommissionStatements.Add(overdue);
            var vadiTracked = await db.BarberShops.FirstAsync(s => s.Id == newShops[3].Shop.Id, ct);
            vadiTracked.Suspend("Ödenmemiş komisyon borcu — demo veri.");

            // shop[6] (Taze) — Paid.
            var paid = CommissionStatement.Create(newShops[6].Shop.Id, periodStart, periodEnd, 1_240_000, rate).Value;
            paid.MarkNotified();
            paid.MarkPaid();
            db.CommissionStatements.Add(paid);

            await db.SaveChangesAsync(ct);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE commission_statements SET due_date = {DateOnly.FromDateTime(now.UtcDateTime).AddDays(5)} WHERE id = {notified.Id}", ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE commission_statements SET due_date = {DateOnly.FromDateTime(now.UtcDateTime).AddDays(-3)} WHERE id = {overdue.Id}", ct);

            logger.LogInformation("RichDemoSeeder: created 3 commission statements (Notified/SuspensionApplied/Paid).");
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
