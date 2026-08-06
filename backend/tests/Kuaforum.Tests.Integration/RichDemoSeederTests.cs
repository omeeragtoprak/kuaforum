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
public sealed class RichDemoSeederTests(IntegrationTestFactory factory)
{
    private const string DemoCustomerPhone = "+905551234567";

    [Fact]
    public async Task SeedAsync_CreatesRichErzurumData_AllBookingStatusesRepresented_AndBillingExamples()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<RichDemoSeeder>();
            await seeder.SeedAsync(default);
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

        // Billing — 3 statement, farklı durumlar.
        var statements = await db.CommissionStatements.AsNoTracking()
            .Where(s => erzurumShops.Select(x => x.Id).Contains(s.BarberShopId))
            .ToListAsync();
        var richStatements = statements.Where(s => s.BasisAmountMinor is 850_000 or 620_000 or 1_240_000).ToList();
        Assert.Equal(3, richStatements.Count);
        Assert.Contains(richStatements, s => s.Status == CommissionStatementStatus.Notified);
        Assert.Contains(richStatements, s => s.Status == CommissionStatementStatus.SuspensionApplied);
        Assert.Contains(richStatements, s => s.Status == CommissionStatementStatus.Paid);
        var suspendedShopId = richStatements.Single(s => s.Status == CommissionStatementStatus.SuspensionApplied).BarberShopId;
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
            await seeder.SeedAsync(default);
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
}
