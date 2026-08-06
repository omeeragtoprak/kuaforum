using System.Reflection;
using Kuaforum.Application.Common;
using Kuaforum.Domain.Modules.Barber;
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
/// Development-only comprehensive seeder.
///
/// Şu zinciri kurar:
///   - 15 ek BarberShop (DemoBarberSeeder'ın 6'sına ek; 15 büyük şehir kapsanır)
///   - 50 müşteri (Customer User; +9051000000XX numara aralığı)
///   - 200+ Booking (her status: Pending, Confirmed, Started, Completed, Cancelled, NoShow)
///   - Captured / Settled / Refunded Payment kayıtları
///   - Approved + Pending + Hidden Review'lar (bazılarında BarberReply)
///   - In-app + SMS + Push NotificationLog'lar
///
/// Idempotent: ilk customer phone (+905100000001) varsa hiç dokunmaz.
/// PRODUCTION: Bu seeder asla çalışmamalı. Seed:DemoBarbers ve Seed:ComprehensiveDemo flag'leri appsettings'te.
/// </summary>
public sealed class ComprehensiveDemoSeeder(ApplicationDbContext db, ILogger<ComprehensiveDemoSeeder> logger)
{
    private const string IdempotencyPhone = "+905100000001";
    private const decimal CommissionRate = 0.10m; // %10 platform commission
    private static readonly Random Rng = new(42); // deterministic seed

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var alreadySeeded = await db.Users.AnyAsync(u => u.PhoneE164 == IdempotencyPhone, ct);
        if (alreadySeeded)
        {
            logger.LogDebug("ComprehensiveDemoSeeder: already seeded, skipping.");
            // RFC-008 öncesi seed edilmiş dev DB'lerde personalar consent'siz olabilir —
            // eksik ToS rızalarını idempotent backfill et.
            await SeedMissingTosConsentsAsync(ct);
            return;
        }

        logger.LogInformation("ComprehensiveDemoSeeder: starting full seed...");

        // Promote any existing demo barber owners (DemoBarberSeeder leaves them as Customer).
        var legacyOwnerPhones = new[]
        {
            "+905001000001", "+905001000002", "+905001000003",
            "+905001000004", "+905001000005", "+905001000006",
        };
        var legacyOwners = await db.Users
            .Where(u => u.PhoneE164 != null && legacyOwnerPhones.Contains(u.PhoneE164) && u.Role == UserRole.Customer)
            .ToListAsync(ct);
        foreach (var legacyOwner in legacyOwners)
        {
            legacyOwner.PromoteToBarberOwner();
        }
        if (legacyOwners.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("ComprehensiveDemoSeeder: promoted {N} legacy demo owners.", legacyOwners.Count);
        }

        var cities = await db.Cities.ToDictionaryAsync(c => c.PlateCode, ct);
        var districts = await db.Districts.AsNoTracking().ToListAsync(ct);
        var districtsByCityAndName = districts
            .GroupBy(d => (d.CityId, d.Name.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.First());

        // ---- Step 1: Additional shops (15 cities) ----
        var newShops = new List<(BarberShop Shop, User Owner, List<Stylist> Stylists, List<ServiceCatalog> Services)>();

        foreach (var demo in AdditionalShops)
        {
            if (!cities.TryGetValue(demo.CityPlate, out var city))
            {
                logger.LogWarning("Comprehensive: city plate {Plate} missing for {Name}, skip.", demo.CityPlate, demo.ShopName);
                continue;
            }
            var districtKey = (city.Id, demo.DistrictName.ToLowerInvariant());
            if (!districtsByCityAndName.TryGetValue(districtKey, out var district))
            {
                logger.LogWarning("Comprehensive: district {District} missing in {City}, skip {Name}.",
                    demo.DistrictName, city.Name, demo.ShopName);
                continue;
            }

            var phoneResult = PhoneE164.TryCreate(demo.OwnerPhone);
            if (phoneResult.IsFailure) continue;

            var ownerResult = User.CreateCustomerByPhone(phoneResult.Value, demo.OwnerName);
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
                // Mahalle opsiyonel (RFC-007) — ilçede mahalle seed'i yoksa null bırak.
                var addressResult = Address.Create(
                    city.Id, district.Id, neighborhood?.Id,
                    demo.AddressLine1, coordResult.Value);
                if (addressResult.IsSuccess) shop.SetAddress(addressResult.Value);
            }
            shop.MarkPendingKyc();
            shop.ApproveKyc(subMerchantKey: $"DEMO-SUB-{demo.Slug}");
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
                    var open = new TimeOnly(9, 0);
                    var close = new TimeOnly(19, 30);
                    var whResult = WorkingHours.Create(stylist.Id, dow, open, close, isClosed);
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

            newShops.Add((shop, owner, stylists, services));
        }

        await db.SaveChangesAsync(ct); // persist new shops first so FK refs work below
        logger.LogInformation("Comprehensive: created {N} new shops.", newShops.Count);

        // ---- Step 2: Customer users ----
        var customers = new List<User>();
        foreach (var c in DemoCustomers)
        {
            var phoneResult = PhoneE164.TryCreate(c.Phone);
            if (phoneResult.IsFailure) continue;
            var userResult = User.CreateCustomerByPhone(phoneResult.Value, c.DisplayName);
            if (userResult.IsFailure) continue;
            var user = userResult.Value;
            db.Users.Add(user);
            customers.Add(user);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Comprehensive: created {N} customers.", customers.Count);

        // ---- Step 3: Build a list of all shops (existing 6 + new 15) for booking allocation ----
        var allShops = await db.BarberShops
            .AsNoTracking()
            .Where(s => s.Status == BarberShopStatus.Active)
            .ToListAsync(ct);

        var shopStylists = await db.Stylists
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(ct);
        var stylistsByShop = shopStylists
            .GroupBy(s => s.BarberShopId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var shopServices = await db.ServiceCatalogs
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(ct);
        var servicesByShop = shopServices
            .GroupBy(s => s.BarberShopId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ---- Step 4: Bookings + payments (lifecycle progressed inline) ----
        var bookings = new List<Booking>();
        var paymentsToAdd = new List<Payment>();
        var reviewsToAdd = new List<Review>();
        var bookingsByCustomer = new Dictionary<Guid, List<Booking>>();

        var now = DateTimeOffset.UtcNow;

        var attemptedCount = 0;
        var skipNoStylist = 0;
        var skipNoService = 0;
        var initFailures = 0;
        var progressExceptions = 0;
        foreach (var customer in customers)
        {
            var customerBookings = new List<Booking>();
            var bookingCount = Rng.Next(3, 8); // 3-7 bookings per customer

            for (var i = 0; i < bookingCount; i++)
            {
                attemptedCount++;
                var shop = allShops[Rng.Next(allShops.Count)];
                if (!stylistsByShop.TryGetValue(shop.Id, out var stylistList) || stylistList.Count == 0) { skipNoStylist++; continue; }
                if (!servicesByShop.TryGetValue(shop.Id, out var serviceList) || serviceList.Count == 0) { skipNoService++; continue; }

                var stylist = stylistList[Rng.Next(stylistList.Count)];
                var serviceCount = Rng.Next(1, Math.Min(3, serviceList.Count) + 1);
                var pickedServices = serviceList
                    .OrderBy(_ => Rng.Next())
                    .Take(serviceCount)
                    .ToList();

                // Decide *desired* schedule (-60d .. +14d) — past values valid because we backdate post-init.
                var dayOffset = Rng.Next(-60, 15);
                var hour = Rng.Next(9, 18);
                var minute = Rng.Next(0, 4) * 15;
                var desiredScheduledAt = new DateTimeOffset(
                    now.Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute),
                    TimeSpan.Zero);
                if (desiredScheduledAt < now.AddMinutes(-1) && dayOffset == 0)
                    desiredScheduledAt = desiredScheduledAt.AddDays(1);

                // Domain rejects past schedules in Booking.Initiate; create at "future-ish" then backdate.
                var initSchedule = desiredScheduledAt > now ? desiredScheduledAt : now.AddHours(2);

                var bookingType = Rng.NextDouble() < 0.05 ? BookingType.WalkIn : BookingType.Scheduled;
                var serviceTuples = pickedServices
                    .Select(s => (s.Id, s.Name, s.DurationMinutes, s.BufferMinutes, s.PriceMinor))
                    .ToList();

                // requiresOnlinePayment: false — demo veri, ExpirePendingBookingsJob'un per-shop
                // dallanmasını test etmiyor (bkz. BookingSlotSafetyTests/PaytrWebhookTests
                // ayrı, gerçekçi kapora-aktif shop fixture'ları kullanıyor).
                var bookingResult = Booking.Initiate(
                    customer.Id, shop.Id, stylist.Id, initSchedule,
                    serviceTuples, bookingType, CommissionRate,
                    requiresOnlinePayment: false,
                    customerNote: Rng.NextDouble() < 0.3 ? PickRandomNote() : null);

                if (bookingResult.IsFailure) { initFailures++; continue; }
                var booking = bookingResult.Value;

                Booking? finalBooking;
                try
                {
                    var (payment, completedTransitions) = ProgressBookingAndPayment(
                        booking, customer.Id, shop.Id, desiredScheduledAt, now);
                    if (payment is not null) paymentsToAdd.Add(payment);

                    if (desiredScheduledAt != initSchedule)
                    {
                        var shift = desiredScheduledAt - initSchedule;
                        BackdateBooking(booking, shift, completedTransitions);
                        if (payment is not null) BackdatePayment(payment, shift);
                    }
                    finalBooking = booking;
                }
                catch (Exception ex)
                {
                    progressExceptions++;
                    logger.LogWarning(ex, "Booking lifecycle progression failed for customer {Cust}", customer.Id);
                    finalBooking = null;
                }

                if (finalBooking is null) continue;
                bookings.Add(finalBooking);
                customerBookings.Add(finalBooking);
            }

            bookingsByCustomer[customer.Id] = customerBookings;
        }

        logger.LogInformation(
            "Comprehensive booking gen: attempted={Attempted} skipNoStylist={NS} skipNoService={NV} initFailures={Init} progressExceptions={Prog} resulting={Result}",
            attemptedCount, skipNoStylist, skipNoService, initFailures, progressExceptions, bookings.Count);

        // ---- Add bookings & payments to context ----
        db.Bookings.AddRange(bookings);
        db.Payments.AddRange(paymentsToAdd);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bookings/Payments SaveChanges failed.");
            throw;
        }
        logger.LogInformation("Comprehensive: persisted {Bookings} bookings, {Payments} payments.", bookings.Count, paymentsToAdd.Count);

        // ---- Step 5: Reviews (only for completed bookings, ~70% of them) ----
        foreach (var booking in bookings.Where(b => b.Status == BookingStatus.Completed))
        {
            if (Rng.NextDouble() > 0.7) continue;

            var rating = WeightedRating();
            var hasBody = Rng.NextDouble() < 0.6;
            var body = hasBody ? PickReviewBody(rating) : null;

            var reviewResult = Review.Submit(
                booking.CustomerId, booking.BarberShopId, booking.StylistId, booking.Id,
                rating, body);
            if (reviewResult.IsFailure) continue;
            var review = reviewResult.Value;

            // 5% manually rejected
            if (Rng.NextDouble() < 0.05)
            {
                review.Reject(Guid.Empty, ReviewRejectionReason.OffTopic);
            }
            // 10% with barber reply
            else if (review.ModerationStatus == ReviewModerationStatus.Approved
                && Rng.NextDouble() < 0.30)
            {
                var ownerOfShop = newShops.FirstOrDefault(s => s.Shop.Id == booking.BarberShopId).Owner
                    ?? await db.Users.AsNoTracking()
                        .Where(u => u.Role == UserRole.BarberOwner)
                        .OrderBy(u => u.Id)
                        .FirstOrDefaultAsync(ct);
                if (ownerOfShop is not null)
                {
                    review.AddReply(ownerOfShop.Id, PickReplyText(rating));
                }
            }

            reviewsToAdd.Add(review);
        }

        db.Reviews.AddRange(reviewsToAdd);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Comprehensive: created {N} reviews.", reviewsToAdd.Count);

        // ---- Step 6: Notifications (sample logs across customers) ----
        var notifs = new List<NotificationLog>();
        foreach (var customer in customers.Take(30))
        {
            var customerBookings = bookingsByCustomer.GetValueOrDefault(customer.Id) ?? new();

            foreach (var b in customerBookings.Where(b => b.Status is BookingStatus.Confirmed or BookingStatus.Completed))
            {
                // Push: booking confirmation
                var pushNotif = NotificationLog.Create(
                    customer.Id, NotificationChannel.Push, "booking.confirmed",
                    recipient: $"fcm-mock-{customer.Id:N}"[..20],
                    subject: "Randevun onaylandı",
                    body: $"Randevun {b.ScheduledAt:dd MMM HH:mm} için onaylandı.");
                pushNotif.MarkSent($"mock-msg-{Guid.NewGuid():N}"[..16]);
                notifs.Add(pushNotif);

                // SMS for older bookings (< 7d)
                if (b.Status == BookingStatus.Completed
                    && customer.PhoneE164 is not null
                    && now - b.ScheduledAt < TimeSpan.FromDays(7))
                {
                    var smsNotif = NotificationLog.Create(
                        customer.Id, NotificationChannel.Sms, "booking.review_request",
                        recipient: customer.PhoneE164,
                        subject: "Yorumun bekleniyor",
                        body: "Randevunuz tamamlandı. Deneyiminizi paylaşır mısınız?");
                    smsNotif.MarkSent($"netgsm-mock-{Guid.NewGuid():N}"[..16]);
                    notifs.Add(smsNotif);
                }
            }

            // In-app welcome
            var welcomeNotif = NotificationLog.Create(
                customer.Id, NotificationChannel.InApp, "system.welcome",
                recipient: customer.Id.ToString(),
                subject: "Kuaforum'a hoş geldin",
                body: "İlk randevunda %10 indirim için profil sayfanı tamamla.");
            welcomeNotif.MarkSent(null);
            notifs.Add(welcomeNotif);
        }

        // Failed notification example (~3 cases for visibility)
        for (var i = 0; i < 3 && i < customers.Count; i++)
        {
            var failed = NotificationLog.Create(
                customers[i].Id, NotificationChannel.Email, "marketing.weekly_digest",
                recipient: $"customer{i}@example.com",
                subject: "Haftalık öneriler",
                body: "Sana özel salonlar...");
            failed.MarkFailed("Mock SMTP timeout (demo)");
            notifs.Add(failed);
        }

        db.NotificationLogs.AddRange(notifs);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Comprehensive: created {N} notification logs.", notifs.Count);

        // ---- Step 7: Audit logs (admin actions sample) ----
        var auditLogs = new List<AuditLog>();
        foreach (var (shop, _, _, _) in newShops.Take(5))
        {
            auditLogs.Add(AuditLog.Create(
                actorUserId: null,
                actorType: AuditActorType.System,
                actionType: "shop.kyc.approved",
                resourceType: "BarberShop",
                resourceId: shop.Id.ToString(),
                payloadJson: $"{{\"slug\":\"{shop.Slug}\",\"sub_merchant\":\"{shop.SubMerchantKey}\"}}"));
        }
        // Sample customer registrations
        foreach (var customer in customers.Take(10))
        {
            auditLogs.Add(AuditLog.Create(
                actorUserId: customer.Id,
                actorType: AuditActorType.User,
                actionType: "user.registered",
                resourceType: "User",
                resourceId: customer.Id.ToString(),
                payloadJson: $"{{\"role\":\"Customer\",\"channel\":\"otp\"}}"));
        }
        db.AuditLogs.AddRange(auditLogs);
        await db.SaveChangesAsync(ct);

        // ---- Step 8: RFC-008 — standart personalar (owner + customer) güncel ToS rızasıyla doğar ----
        await SeedMissingTosConsentsAsync(ct);

        logger.LogInformation("ComprehensiveDemoSeeder: complete. shops={Shops} customers={Customers} bookings={Bookings} payments={Payments} reviews={Reviews} notifs={Notifs}",
            newShops.Count, customers.Count, bookings.Count, paymentsToAdd.Count, reviewsToAdd.Count, notifs.Count);
    }

    /// <summary>
    /// RFC-008 — bu seeder'ın yarattığı standart personalara (15 shop owner + 50 customer)
    /// güncel versiyonla ToS rızası seed eder; consent gate demo hesaplarını bloklamasın.
    /// Rıza akışının elle test kapısı consent'siz <c>DevLoginRole.CustomerFresh</c>'tir.
    /// Versiyon <see cref="LegalDocuments.CurrentTosVersion"/> sabitinden okunur.
    /// Idempotent: güncel versiyonla granted ToS'u olan kullanıcı atlanır.
    /// </summary>
    private async Task SeedMissingTosConsentsAsync(CancellationToken ct)
    {
        var personaPhones = AdditionalShops.Select(s => s.OwnerPhone)
            .Concat(DemoCustomers.Select(c => c.Phone))
            .ToArray();

        var userIds = await db.Users
            .Where(u => u.PhoneE164 != null && personaPhones.Contains(u.PhoneE164))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (userIds.Count == 0) return;

        var alreadyConsented = await db.Consents
            .Where(c => userIds.Contains(c.UserId)
                && c.Purpose == ConsentPurpose.TermsOfService
                && c.Granted
                && c.RevokedAt == null
                && c.Version == LegalDocuments.CurrentTosVersion)
            .Select(c => c.UserId)
            .ToListAsync(ct);

        var missing = userIds.Except(alreadyConsented).ToList();
        if (missing.Count == 0) return;

        foreach (var userId in missing)
        {
            db.Consents.Add(Consent.Grant(
                userId, ConsentPurpose.TermsOfService, LegalDocuments.CurrentTosVersion, ConsentSource.Registration));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("ComprehensiveDemoSeeder: {Count} demo persona için ToS consent seed edildi.", missing.Count);
    }

    /// <summary>
    /// Progresses booking through lifecycle states based on desired (possibly past) schedule.
    /// Returns optional payment + a flags struct describing which transitions happened
    /// (so backdating can know what timestamps to shift).
    /// </summary>
    private static (Payment? Payment, BookingTransitions Transitions) ProgressBookingAndPayment(
        Booking booking,
        Guid customerId,
        Guid shopId,
        DateTimeOffset desiredScheduledAt,
        DateTimeOffset now)
    {
        var transitions = new BookingTransitions();
        var hoursDiff = (desiredScheduledAt - now).TotalHours;

        Payment CreateAndCapture()
        {
            // RFC-012: komisyon ödeme anında kesilmiyor artık (ay sonu CommissionStatement'a
            // taşındı) — booking.PlatformCommissionMinor buraya taşınmaz, sabit 0.
            var p = Payment.Initiate(
                booking.Id, customerId, shopId,
                booking.TotalAmountMinor, platformCommissionMinor: 0,
                PaymentProviderName.Mock,
                idempotencyKey: $"seed-{booking.Id:N}").Value;
            var pspMdr = booking.TotalAmountMinor * 18 / 1000;
            var txId = $"DEMO-TX-{booking.Id:N}";
            p.Capture(txId.Length > 32 ? txId[..32] : txId, pspMdr);
            return p;
        }

        // RFC-013 C4: ödeme artık booking'i tek başına Confirm ETMEZ — demo veri gerçekçiliği
        // için burada da gerçek iki-adımlı akış simüle edilir (MarkPaymentReceived, sonra ayrı
        // bir OwnerConfirm çağrısı — gerçek owner onayının yerine geçen sentetik adım).
        void MarkPaidAndConfirm(Payment payment)
        {
            booking.MarkPaymentReceived(payment.Id);
            booking.OwnerConfirm();
        }

        if (hoursDiff < -1)
        {
            // Past: Completed (65%), Cancelled (20%), NoShow (15%)
            var roll = Rng.NextDouble();
            if (roll < 0.65)
            {
                var payment = CreateAndCapture();
                MarkPaidAndConfirm(payment); transitions.Confirmed = true;
                booking.Start();              transitions.Started = true;
                booking.Complete();           transitions.Completed = true;
                if (now - desiredScheduledAt > TimeSpan.FromDays(1))
                    payment.MarkSettled();
                return (payment, transitions);
            }
            else if (roll < 0.85)
            {
                var payment = CreateAndCapture();
                MarkPaidAndConfirm(payment); transitions.Confirmed = true;
                booking.Cancel(BookingCancellationReason.CustomerRequested);
                transitions.Cancelled = true;
                if (booking.RefundPercentage is > 0)
                {
                    var refundAmount = booking.TotalAmountMinor * booking.RefundPercentage.Value / 100;
                    var rfId = $"DEMO-RF-{booking.Id:N}";
                    payment.AddRefund(refundAmount, RefundReason.CustomerRequested,
                        providerRefundId: rfId.Length > 32 ? rfId[..32] : rfId);
                }
                return (payment, transitions);
            }
            else
            {
                var payment = CreateAndCapture();
                MarkPaidAndConfirm(payment); transitions.Confirmed = true;
                // For NoShow we need ScheduledEndsAt < now after backdating, which we'll have
                // because desiredScheduledAt is in the past. But MarkNoShow is checked against
                // current ScheduledEndsAt (still future since booking was init at +2h).
                // So we'll mark Cancelled here — backdating will let us reach NoShow status
                // via direct status field set if desired. Simpler: stay as Cancelled.
                booking.Cancel(BookingCancellationReason.CustomerRequested);
                transitions.Cancelled = true;
                return (payment, transitions);
            }
        }
        else if (hoursDiff < 1)
        {
            var payment = CreateAndCapture();
            MarkPaidAndConfirm(payment); transitions.Confirmed = true;
            if (Rng.NextDouble() < 0.5)
            {
                booking.Start();
                transitions.Started = true;
            }
            return (payment, transitions);
        }
        else
        {
            if (Rng.NextDouble() < 0.45)
            {
                var payment = CreateAndCapture();
                MarkPaidAndConfirm(payment); transitions.Confirmed = true;
                return (payment, transitions);
            }
            return (null, transitions);
        }
    }

    private struct BookingTransitions
    {
        public bool Confirmed;
        public bool Started;
        public bool Completed;
        public bool Cancelled;
    }

    private static void BackdateBooking(Booking booking, TimeSpan shift, BookingTransitions transitions)
    {
        ShiftField(booking, "ScheduledAt", shift);
        ShiftField(booking, "ScheduledEndsAt", shift);
        ShiftField(booking, "CreatedAt", shift);
        ShiftField(booking, "UpdatedAt", shift);
        if (transitions.Confirmed) ShiftField(booking, "ConfirmedAt", shift);
        if (transitions.Confirmed) ShiftField(booking, "DepositPaidAt", shift); // MarkPaidAndConfirm sets both at seed-time
        if (transitions.Started)   ShiftField(booking, "StartedAt", shift);
        if (transitions.Completed) ShiftField(booking, "CompletedAt", shift);
        if (transitions.Cancelled) ShiftField(booking, "CancelledAt", shift);
    }

    private static void BackdatePayment(Payment payment, TimeSpan shift)
    {
        ShiftField(payment, "CreatedAt", shift);
        ShiftField(payment, "UpdatedAt", shift);
        ShiftField(payment, "AuthorizedAt", shift);
        ShiftField(payment, "CapturedAt", shift);
        ShiftField(payment, "SettledAt", shift);
        foreach (var refund in payment.Refunds)
        {
            ShiftField(refund, "CreatedAt", shift);
            ShiftField(refund, "UpdatedAt", shift);
            ShiftField(refund, "CompletedAt", shift);
        }
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
                // nullable that's null — nothing to shift
                break;
            default:
                if (current is ValueType && field.FieldType == typeof(DateTimeOffset?))
                {
                    var nullable = (DateTimeOffset?)current;
                    if (nullable.HasValue)
                        field.SetValue(entity, (DateTimeOffset?)(nullable.Value + shift));
                }
                break;
        }
    }

    private static FieldInfo? FindBackingField(Type type, string propName)
    {
        var current = type;
        while (current is not null && current != typeof(object))
        {
            var field = current.GetField($"<{propName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null) return field;
            current = current.BaseType;
        }
        return null;
    }

    private static int WeightedRating()
    {
        // distribution: 5★=55%, 4★=25%, 3★=12%, 2★=5%, 1★=3%
        var roll = Rng.NextDouble();
        return roll switch
        {
            < 0.55 => 5,
            < 0.80 => 4,
            < 0.92 => 3,
            < 0.97 => 2,
            _ => 1,
        };
    }

    private static string PickRandomNote()
    {
        var notes = new[]
        {
            "Sakal şeklim biraz daha kısa olsun lütfen.",
            "Geçen seferki gibi yapabilir misin?",
            "Boyada amonyak istemiyorum, hassasım.",
            "Yıkama kısa olsun, alerjim var.",
            "İlk defa geleceğim, kısa görüşürüz.",
            "Özel günüm var, çok şık olsun.",
            "Çocuğumla birlikte geleceğim.",
        };
        return notes[Rng.Next(notes.Length)];
    }

    private static string PickReviewBody(int rating)
    {
        return rating switch
        {
            5 => Sample(new[]
            {
                "Süper memnun kaldım, kesinlikle tavsiye ederim. Stilist çok ilgili ve hızlıydı.",
                "Salon temiz, çay ikram edildi, kesim mükemmel oldu. Kesin yine geleceğim.",
                "Tam istediğim gibi yaptılar. Fiyat çok uygun, randevu sistemi de çok kullanışlı.",
                "Yıllarca aynı yere gittim, ilk defa burayı denedim ve çok beğendim.",
                "Mehmet usta efsane, sakal şekillendirme harika.",
            }),
            4 => Sample(new[]
            {
                "Genel olarak güzeldi, biraz hızlı bitti. Yine gelirim.",
                "Kesim güzel ama beklemem gerekti, randevu saati biraz kayma yapmış.",
                "Salon güzel, kesim iyi. Fiyat biraz yüksek olmuş ama sorun değil.",
            }),
            3 => Sample(new[]
            {
                "Beklediğim kadar değildi ama vasat sayılır.",
                "Kesim iyi ama fön kısmı eksik kaldı.",
                "Salon kalabalıktı, biraz daha sessiz olabilirdi.",
            }),
            2 => Sample(new[]
            {
                "Beklediğim gibi çıkmadı, modeli anlatmam zor oldu.",
                "Çok bekledim, kesim de aceleye geldi.",
            }),
            1 => Sample(new[]
            {
                "Hayal kırıklığıydı, bir daha gelmem.",
                "Saçımı düzeltmem için başka bir yere gitmem gerekti.",
            }),
            _ => "Yorum",
        };
    }

    private static string PickReplyText(int rating)
    {
        return rating >= 4
            ? Sample(new[]
            {
                "Teşekkürler, yine bekleriz!",
                "Geri bildiriminiz için teşekkür ederiz, görüşmek üzere.",
                "Memnuniyetinize sevindik, sevgilerimizle.",
            })
            : Sample(new[]
            {
                "Geri bildiriminiz için teşekkürler, daha iyisini sunabilmek için çalışacağız.",
                "Üzgünüz, ekibimizle paylaştık. Bir sonraki ziyaretinizde telafi etmek isteriz.",
            });
    }

    private static string Sample(string[] options) => options[Rng.Next(options.Length)];

    // ============================================================
    //                        STATIC DEMO DATA
    // ============================================================

    private record AdditionalShop(
        int CityPlate,
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

    private static readonly AdditionalShop[] AdditionalShops =
    {
        new(
            CityPlate: 16, DistrictName: "Nilüfer", Slug: "nilufer-prestij",
            ShopName: "Prestij Erkek Kuaförü", Description: "Bursa Nilüfer'in modern erkek kuaförü.",
            ShopPhone: "+902241000001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Beşevler Cad. No:14", Latitude: 40.2197, Longitude: 28.9911,
            OwnerName: "Burak Prestij", OwnerPhone: "+905001000007",
            Stylists: new[] { "Burak Prestij", "Cengiz Berber" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 220),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 130),
                ("Saç + Sakal", ServiceGender.Male, 35, 320),
                ("Saç Yıkama", ServiceGender.Male, 10, 70),
                ("Yüz Bakımı", ServiceGender.Male, 30, 250),
            }),

        new(
            CityPlate: 16, DistrictName: "Osmangazi", Slug: "osmangazi-glow",
            ShopName: "Glow Beauty Salon", Description: "Osmangazi'de saç + cilt + manikür.",
            ShopPhone: "+902241000002", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Heykel Meydanı No:7", Latitude: 40.1828, Longitude: 29.0639,
            OwnerName: "Selin Işık", OwnerPhone: "+905001000008",
            Stylists: new[] { "Selin Işık", "Buse Renk", "Pelin Bakım" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 320),
                ("Saç Boyama", ServiceGender.Female, 90, 950),
                ("Balyaj", ServiceGender.Female, 180, 2200),
                ("Manikür", ServiceGender.Female, 30, 180),
                ("Cilt Bakımı", ServiceGender.Female, 60, 700),
                ("Kaş Şekillendirme", ServiceGender.Female, 15, 120),
            }),

        new(
            CityPlate: 7, DistrictName: "Muratpaşa", Slug: "muratpasa-marina",
            ShopName: "Marina Hair Studio", Description: "Antalya kıyıda lüks unisex stüdyo.",
            ShopPhone: "+902421000001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Cumhuriyet Cad. No:41", Latitude: 36.8841, Longitude: 30.7056,
            OwnerName: "Deniz Marina", OwnerPhone: "+905001000009",
            Stylists: new[] { "Deniz Marina", "Aylin Yelken", "Kerem Stilist" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 30, 280),
                ("Kadın Saç Kesim", ServiceGender.Female, 45, 450),
                ("Saç Boyama", ServiceGender.Female, 90, 1500),
                ("Keratin Bakım", ServiceGender.Female, 120, 1800),
                ("Sakal Tıraşı", ServiceGender.Male, 20, 180),
            }),

        new(
            CityPlate: 7, DistrictName: "Konyaaltı", Slug: "konyaalti-rapid",
            ShopName: "Rapid Berber", Description: "Konyaaltı plaj çevresi hızlı erkek kuaförü.",
            ShopPhone: "+902421000002", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Akdeniz Bulvarı No:212", Latitude: 36.8541, Longitude: 30.6386,
            OwnerName: "Onur Rapid", OwnerPhone: "+905001000010",
            Stylists: new[] { "Onur Rapid", "Tuğrul Çırak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 20, 150),
                ("Sakal", ServiceGender.Male, 12, 80),
                ("Saç + Sakal Kombo", ServiceGender.Male, 30, 200),
            }),

        new(
            CityPlate: 1, DistrictName: "Seyhan", Slug: "seyhan-pamuk",
            ShopName: "Pamuk Kuaför", Description: "Adana merkez Seyhan'da bayan kuaförü.",
            ShopPhone: "+903221000001", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Atatürk Cad. No:89", Latitude: 37.0017, Longitude: 35.3289,
            OwnerName: "Pınar Pamuk", OwnerPhone: "+905001000011",
            Stylists: new[] { "Pınar Pamuk", "Aslı Boya" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 250),
                ("Saç Boyama", ServiceGender.Female, 90, 800),
                ("Manikür", ServiceGender.Female, 30, 150),
                ("Pedikür", ServiceGender.Female, 45, 200),
                ("Fön", ServiceGender.Female, 30, 150),
            }),

        new(
            CityPlate: 42, DistrictName: "Selçuklu", Slug: "selcuklu-mevlana",
            ShopName: "Mevlana Berberi", Description: "Konya Selçuklu klasik erkek berberi.",
            ShopPhone: "+903321000001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Karatay Bulvarı No:55", Latitude: 37.9095, Longitude: 32.4775,
            OwnerName: "Tarık Berber", OwnerPhone: "+905001000012",
            Stylists: new[] { "Tarık Berber", "Osman Çırak", "İbrahim Usta" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 170),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 90),
                ("Klasik Tıraş Ustura", ServiceGender.Male, 30, 220),
                ("Saç + Sakal", ServiceGender.Male, 35, 230),
            }),

        new(
            CityPlate: 27, DistrictName: "Şahinbey", Slug: "sahinbey-elegance",
            ShopName: "Elegance Beauty", Description: "Gaziantep'in modern bayan kuaförü.",
            ShopPhone: "+903421000001", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "İncilipınar Mah. No:23", Latitude: 37.0660, Longitude: 37.3781,
            OwnerName: "Ezgi Elegance", OwnerPhone: "+905001000013",
            Stylists: new[] { "Ezgi Elegance", "Sevda Renk" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 230),
                ("Saç Boyama", ServiceGender.Female, 90, 850),
                ("Balyaj", ServiceGender.Female, 180, 1800),
                ("Saç Bakım", ServiceGender.Female, 45, 350),
                ("Manikür", ServiceGender.Female, 30, 150),
            }),

        new(
            CityPlate: 38, DistrictName: "Melikgazi", Slug: "melikgazi-erciyes",
            ShopName: "Erciyes Erkek Kuaförü", Description: "Kayseri Melikgazi'de aile berberi.",
            ShopPhone: "+903521000001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Sivas Cad. No:147", Latitude: 38.7322, Longitude: 35.4853,
            OwnerName: "Recep Erciyes", OwnerPhone: "+905001000014",
            Stylists: new[] { "Recep Erciyes", "Hakan Çırak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 180),
                ("Sakal", ServiceGender.Male, 15, 100),
                ("Yüz Bakımı", ServiceGender.Male, 30, 220),
                ("Saç + Sakal", ServiceGender.Male, 35, 250),
            }),

        new(
            CityPlate: 33, DistrictName: "Yenişehir", Slug: "yenisehir-deniz",
            ShopName: "Deniz Stüdyo", Description: "Mersin Yenişehir unisex saç stüdyosu.",
            ShopPhone: "+903241000001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Forum Cad. No:18", Latitude: 36.7894, Longitude: 34.5742,
            OwnerName: "Cem Deniz", OwnerPhone: "+905001000015",
            Stylists: new[] { "Cem Deniz", "Selma Akın" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 25, 190),
                ("Kadın Saç Kesim", ServiceGender.Female, 40, 280),
                ("Saç Boyama", ServiceGender.Female, 90, 900),
                ("Sakal", ServiceGender.Male, 15, 100),
            }),

        new(
            CityPlate: 26, DistrictName: "Tepebaşı", Slug: "tepebasi-kampus",
            ShopName: "Kampüs Hızlı Kuaför", Description: "Eskişehir öğrenci dostu kuaför.",
            ShopPhone: "+902221000001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "İki Eylül Cad. No:73", Latitude: 39.7861, Longitude: 30.5108,
            OwnerName: "Murat Kampüs", OwnerPhone: "+905001000016",
            Stylists: new[] { "Murat Kampüs", "Gizem Hızlı" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 20, 130),
                ("Kadın Saç Kesim", ServiceGender.Female, 30, 180),
                ("Sakal", ServiceGender.Male, 15, 80),
                ("Hızlı Fön", ServiceGender.Female, 20, 100),
            }),

        new(
            CityPlate: 61, DistrictName: "Ortahisar", Slug: "ortahisar-karadeniz",
            ShopName: "Karadeniz Berberhanesi", Description: "Trabzon merkez klasik erkek berberi.",
            ShopPhone: "+904621000001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Uzun Sokak No:34", Latitude: 41.0050, Longitude: 39.7222,
            OwnerName: "Yusuf Karadeniz", OwnerPhone: "+905001000017",
            Stylists: new[] { "Yusuf Karadeniz", "Erkan Çırak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 160),
                ("Sakal", ServiceGender.Male, 15, 90),
                ("Yıkama", ServiceGender.Male, 10, 50),
                ("Saç + Sakal", ServiceGender.Male, 35, 220),
            }),

        new(
            CityPlate: 55, DistrictName: "İlkadım", Slug: "ilkadim-yeni",
            ShopName: "Yeni Stil Kuaför", Description: "Samsun merkez bayan kuaförü.",
            ShopPhone: "+903621000001", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Gazi Cad. No:91", Latitude: 41.2867, Longitude: 36.3300,
            OwnerName: "Ayten Yeni", OwnerPhone: "+905001000018",
            Stylists: new[] { "Ayten Yeni", "Hülya Boya" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 250),
                ("Saç Boyama", ServiceGender.Female, 90, 800),
                ("Manikür", ServiceGender.Female, 30, 160),
                ("Pedikür", ServiceGender.Female, 45, 220),
                ("Fön", ServiceGender.Female, 30, 150),
            }),

        new(
            CityPlate: 21, DistrictName: "Yenişehir", Slug: "yenisehir-dicle",
            ShopName: "Dicle Berber", Description: "Diyarbakır Yenişehir merkezde klasik berberhane.",
            ShopPhone: "+904121000001", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Ofis Cad. No:5", Latitude: 37.9214, Longitude: 40.2317,
            OwnerName: "Selim Dicle", OwnerPhone: "+905001000019",
            Stylists: new[] { "Selim Dicle", "Mesut Çırak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 150),
                ("Sakal", ServiceGender.Male, 15, 80),
                ("Saç + Sakal", ServiceGender.Male, 35, 210),
            }),

        new(
            CityPlate: 54, DistrictName: "Adapazarı", Slug: "adapazari-merkez",
            ShopName: "Merkez Stil Salon", Description: "Sakarya Adapazarı'nda unisex modern salon.",
            ShopPhone: "+902641000001", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Sakarya Cad. No:62", Latitude: 40.7842, Longitude: 30.4031,
            OwnerName: "Nazlı Merkez", OwnerPhone: "+905001000020",
            Stylists: new[] { "Nazlı Merkez", "Burak Stilist", "İrem Renk" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 25, 180),
                ("Kadın Saç Kesim", ServiceGender.Female, 40, 270),
                ("Saç Boyama", ServiceGender.Female, 90, 850),
                ("Sakal", ServiceGender.Male, 15, 90),
                ("Manikür", ServiceGender.Female, 30, 170),
            }),

        new(
            CityPlate: 35, DistrictName: "Konak", Slug: "konak-kordon",
            ShopName: "Kordon Beauty", Description: "İzmir Konak Kordon yakını lüks salon.",
            ShopPhone: "+902321000005", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Atatürk Cad. No:228", Latitude: 38.4192, Longitude: 27.1287,
            OwnerName: "Banu Kordon", OwnerPhone: "+905001000021",
            Stylists: new[] { "Banu Kordon", "Doğa Tarz", "Kaan Kesim" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 30, 280),
                ("Kadın Saç Kesim", ServiceGender.Female, 50, 480),
                ("Balyaj", ServiceGender.Female, 180, 2400),
                ("Keratin Bakım", ServiceGender.Female, 120, 1900),
                ("Saç + Sakal Erkek", ServiceGender.Male, 40, 380),
                ("Cilt Bakımı", ServiceGender.Female, 60, 800),
            }),
    };

    private record DemoCustomer(string Phone, string DisplayName);

    private static readonly DemoCustomer[] DemoCustomers =
    {
        new("+905100000001", "Ahmet Yılmaz"),
        new("+905100000002", "Ayşe Demir"),
        new("+905100000003", "Mehmet Kaya"),
        new("+905100000004", "Fatma Şahin"),
        new("+905100000005", "Mustafa Öztürk"),
        new("+905100000006", "Emine Aydın"),
        new("+905100000007", "Hasan Yıldız"),
        new("+905100000008", "Hatice Aslan"),
        new("+905100000009", "Hüseyin Doğan"),
        new("+905100000010", "Zeynep Çelik"),
        new("+905100000011", "İbrahim Çetin"),
        new("+905100000012", "Elif Arslan"),
        new("+905100000013", "Murat Polat"),
        new("+905100000014", "Aylin Korkmaz"),
        new("+905100000015", "Ali Erdoğan"),
        new("+905100000016", "Sevda Türk"),
        new("+905100000017", "Cem Yıldırım"),
        new("+905100000018", "Burcu Acar"),
        new("+905100000019", "Onur Yalçın"),
        new("+905100000020", "Selin Sezer"),
        new("+905100000021", "Berk Aktaş"),
        new("+905100000022", "Pelin Erdem"),
        new("+905100000023", "Tolga Bulut"),
        new("+905100000024", "Gizem Sağlam"),
        new("+905100000025", "Kerem Bayrak"),
        new("+905100000026", "Deniz Sarı"),
        new("+905100000027", "Burak Aksoy"),
        new("+905100000028", "Ezgi Karadağ"),
        new("+905100000029", "Tarık Sönmez"),
        new("+905100000030", "Buse Pekkan"),
        new("+905100000031", "Volkan Gül"),
        new("+905100000032", "Merve Aksoy"),
        new("+905100000033", "Erkan Tunç"),
        new("+905100000034", "İrem Acar"),
        new("+905100000035", "Yusuf Bal"),
        new("+905100000036", "Pınar Gündüz"),
        new("+905100000037", "Cansu Akın"),
        new("+905100000038", "Furkan Aslan"),
        new("+905100000039", "Tuğçe Yıldırım"),
        new("+905100000040", "Serkan Ercan"),
        new("+905100000041", "Hande Korkmaz"),
        new("+905100000042", "Emre Ay"),
        new("+905100000043", "Sıla Doğan"),
        new("+905100000044", "Ozan Yılmaz"),
        new("+905100000045", "Begüm Arslan"),
        new("+905100000046", "Mert Çelik"),
        new("+905100000047", "Yasemin Şen"),
        new("+905100000048", "Doğukan Aydın"),
        new("+905100000049", "Sude Erdem"),
        new("+905100000050", "Hakan Akın"),
    };
}
