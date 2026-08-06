using System.Net;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-012 faturalama omurgası (C1+C2). DemoBarberSeeder hiçbir Booking satırı seed ETMEZ
/// (yalnız shop/stylist/service/working-hours) — bu yüzden CloseMonthlyCommissionStatementsJob
/// yalnızca bu testlerin doğrudan DB'ye eklediği booking'leri görür, 13 demo shop'un geri kalanı
/// matrahsız kalıp atlanır (sıfır-matrah skip'i böylece dolaylı olarak da kanıtlanır).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CommissionBillingTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StatementDto(
        Guid Id, DateOnly PeriodStart, DateOnly PeriodEnd, long BasisAmountMinor, decimal RateApplied,
        long AmountDueMinor, DateOnly DueDate, CommissionStatementStatus Status, bool IsDisputed, string? DisputeNote);
    private sealed record OwnerStatementsPage(IReadOnlyList<StatementDto> Items, string? CommissionPaymentIban, string? CommissionPaymentBankName, string? CommissionPaymentAccountHolderName);
    private sealed record AdminStatementItem(Guid Id, Guid BarberShopId, string BarberShopName, DateOnly PeriodStart, DateOnly PeriodEnd, long AmountDueMinor, DateOnly DueDate, CommissionStatementStatus Status, bool IsDisputed);
    private sealed record AdminStatementsPage(IReadOnlyList<AdminStatementItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record InitiateCommissionPaymentResp(Guid CommissionPaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt, long AmountMinor);
    private sealed record SearchItemDto(Guid Id, string Name);
    private sealed record SearchResp(IReadOnlyList<SearchItemDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record ShopFixture(
        HttpClient OwnerClient, HttpClient AdminClient, HttpClient CustomerClient,
        Guid ShopId, Guid StylistId, Guid ServiceId, string ServiceName, int Duration, int Buffer, long Price);

    // ── Fixture setup ───────────────────────────────────────────────────

    private async Task<ShopFixture> SeedShopAndAuthAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailRes = EmailAddress.TryCreate("admin-billing@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Test Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync();
        }

        var ownerClient = factory.CreateClient();
        var adminClient = factory.CreateClient();
        var customerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        Guid stylistId, serviceId;
        string serviceName;
        int duration, buffer;
        long price;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stylist = await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
            var service = await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
            stylistId = stylist.Id;
            serviceId = service.Id;
            serviceName = service.Name;
            duration = service.DurationMinutes;
            buffer = service.BufferMinutes;
            price = service.PriceMinor;
        }

        return new ShopFixture(ownerClient, adminClient, customerClient, ownerShop.Id, stylistId, serviceId, serviceName, duration, buffer, price);
    }

    private async Task<Guid> CreateBookingAsync(ShopFixture f, BookingStatus targetStatus, DateTimeOffset? completedAtUtc = null, DateTimeOffset? scheduledAtOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var scheduledAt = scheduledAtOverride ?? FutureWeekdayUtc(120);
        var services = new (Guid, string, int, int, long)[] { (f.ServiceId, f.ServiceName, f.Duration, f.Buffer, f.Price) };
        var booking = BookingAggregate.Initiate(Guid.NewGuid(), f.ShopId, f.StylistId, scheduledAt, services, BookingType.Scheduled, 0.10m).Value;

        switch (targetStatus)
        {
            case BookingStatus.Pending:
                break;
            case BookingStatus.Cancelled:
                Assert.True(booking.Cancel(BookingCancellationReason.CustomerRequested).IsSuccess);
                break;
            case BookingStatus.Completed:
                Assert.True(booking.OwnerConfirm().IsSuccess);
                Assert.True(booking.Start().IsSuccess);
                Assert.True(booking.Complete().IsSuccess);
                break;
            case BookingStatus.NoShow:
                Assert.True(booking.OwnerConfirm().IsSuccess);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(targetStatus));
        }

        db.Set<BookingAggregate>().Add(booking);
        await db.SaveChangesAsync();

        // NoShow domain transition requires ScheduledEndsAt to already be past (see
        // Booking.MarkNoShow) — fixture only needs the terminal Status, so it's set directly
        // (same "arrange via raw SQL" pattern BookingSlotSafetyTests uses for CreatedAt backdating).
        if (targetStatus == BookingStatus.NoShow)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE bookings SET status = {(int)BookingStatus.NoShow} WHERE id = {booking.Id}");
        }
        if (targetStatus == BookingStatus.Completed && completedAtUtc.HasValue)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE bookings SET completed_at = {completedAtUtc.Value} WHERE id = {booking.Id}");
        }

        return booking.Id;
    }

    /// <summary>Doğrudan agregat üretimi + Notified'a taşıma — CloseMonthlyCommissionStatementsJob'u yeniden çalıştırmadan Overdue/Suspend/Payment testleri için sabit bir başlangıç durumu kurar.</summary>
    private async Task<Guid> CreateNotifiedStatementAsync(ShopFixture f, long basisAmountMinor, decimal rate, DateOnly? dueDateOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (periodStart, periodEnd) = LastMonthPeriod();

        var statement = CommissionStatement.Create(f.ShopId, periodStart, periodEnd, basisAmountMinor, rate).Value;
        statement.MarkNotified();
        db.Set<CommissionStatement>().Add(statement);
        await db.SaveChangesAsync();

        if (dueDateOverride.HasValue)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commission_statements SET due_date = {dueDateOverride.Value} WHERE id = {statement.Id}");
        }

        return statement.Id;
    }

    /// <summary>
    /// Domain aggregate'ini doğrudan Overdue durumuna taşıyıp kaydeder (job'u hiç çalıştırmadan) —
    /// SuspendOverdueCommissionAccountsJob'un "zaten Overdue'da asılı kalmış" retry senaryosunu
    /// ve ExtendDueDate'in Overdue→Notified geri alma davranışını job'un kendi Notified→Overdue
    /// adımından bağımsız test edebilmek için.
    /// </summary>
    private async Task<Guid> CreateOverdueStatementDirectlyAsync(ShopFixture f, long basisAmountMinor, decimal rate, DateOnly dueDate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (periodStart, periodEnd) = LastMonthPeriod();

        var statement = CommissionStatement.Create(f.ShopId, periodStart, periodEnd, basisAmountMinor, rate).Value;
        statement.MarkNotified();
        statement.MarkOverdue();
        db.Set<CommissionStatement>().Add(statement);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commission_statements SET due_date = {dueDate} WHERE id = {statement.Id}");
        return statement.Id;
    }

    /// <summary>
    /// DevLoginAsync("BarberOwner") her zaman AYNI sabit demo owner'a giriş yapar
    /// (DevLoginCommandHandler.BarberOwnerDemoPhone) — cross-owner IDOR testi için İKİNCİ,
    /// gerçekten farklı bir owner+shop gerekir. Bu owner OTP/dev-login akışından geçmiyor;
    /// token'ı doğrudan IJwtTokenService ile mint ediyoruz (ShopSuspensionTests'in admin
    /// kullanıcısını OTP'siz doğrudan DB'ye ekleme deseninin bir uzantısı).
    /// </summary>
    private async Task<HttpClient> CreateSecondOwnerWithOwnShopAsync()
    {
        Guid ownerBId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var phoneRes = PhoneE164.TryCreate("+905009999998");
            var ownerBRes = Kuaforum.Domain.Modules.Identity.User.CreateCustomerByPhone(phoneRes.Value, "Demo Owner B");
            var ownerB = ownerBRes.Value;
            ownerB.PromoteToBarberOwner();
            db.Users.Add(ownerB);

            var shopBRes = BarberShop.Create(ownerB.Id, "Owner B Shop", SalonGenderType.Unisex);
            db.Set<BarberShop>().Add(shopBRes.Value);
            await db.SaveChangesAsync();
            ownerBId = ownerB.Id;
        }

        using var authScope = factory.Services.CreateScope();
        var authDb = authScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtService = authScope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var ownerBUser = await authDb.Set<Kuaforum.Domain.Modules.Identity.User>().AsNoTracking().FirstAsync(u => u.Id == ownerBId);
        var tokens = jwtService.Issue(ownerBUser);

        var client = factory.CreateClient();
        client.SetBearer(tokens.AccessToken);
        return client;
    }

    private static (DateOnly PeriodStart, DateOnly PeriodEnd) LastMonthPeriod()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>BookingSlotSafetyTests.FutureWeekdayUtc ile aynı — Pazar'ı atlayan deterministik gelecek tarih.</summary>
    private static DateTimeOffset FutureWeekdayUtc(int minDaysAhead)
    {
        var date = DateTime.UtcNow.Date;
        var remaining = minDaysAhead;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek != DayOfWeek.Sunday) remaining--;
        }
        return new DateTimeOffset(date.Year, date.Month, date.Day, 14, 0, 0, TimeSpan.Zero);
    }

    // ── C1: kapanış job'u — matrah kapsamı ─────────────────────────────

    [Fact]
    public async Task CloseMonthlyCommissionStatementsJob_OnlyCountsCompletedBookingsWithinPeriod()
    {
        var f = await SeedShopAndAuthAsync();
        await f.AdminClient.PutAsJsonAsync("/v1/admin/billing/settings", new
        {
            commissionRateGlobal = 0.20m,
            commissionPaymentIban = (string?)null,
            commissionPaymentBankName = (string?)null,
            commissionPaymentAccountHolderName = (string?)null,
        });

        var (periodStart, _) = LastMonthPeriod();
        var completedAtInPeriod = new DateTimeOffset(periodStart.Year, periodStart.Month, periodStart.Day, 12, 0, 0, TimeSpan.Zero).AddDays(5);
        var completedAtThisMonth = DateTimeOffset.UtcNow; // outside last-month period

        await CreateBookingAsync(f, BookingStatus.Completed, completedAtInPeriod); // ✓ counts
        await CreateBookingAsync(f, BookingStatus.Cancelled);                       // ✗ excluded
        await CreateBookingAsync(f, BookingStatus.NoShow);                          // ✗ excluded
        await CreateBookingAsync(f, BookingStatus.Pending);                         // ✗ excluded
        await CreateBookingAsync(f, BookingStatus.Completed, completedAtThisMonth); // ✗ Completed but wrong period

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlyCommissionStatementsJob>();
            await job.RunAsync(default);
        }

        var page = await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements");
        var statement = Assert.Single(page.Items);
        Assert.Equal(f.Price, statement.BasisAmountMinor); // yalnız TEK Completed-in-period booking
        Assert.Equal(periodStart, statement.PeriodStart);
        Assert.Equal(CommissionStatementStatus.Notified, statement.Status);
        Assert.Equal(Kuaforum.Domain.Common.PaymentAmounts.CalculateCommissionMinor(f.Price, 0.20m), statement.AmountDueMinor);

        // Diğer 12 demo shop + bu shop'un dışlanan booking'leri gürültü yaratmadı —
        // toplamda TEK statement (sıfır-matrahlı shop'lar dolaylı olarak atlandı).
        var adminPage = await f.AdminClient.GetJson<AdminStatementsPage>("/v1/admin/billing/statements");
        Assert.Equal(1, adminPage.TotalCount);
    }

    [Fact]
    public async Task CloseMonthlyCommissionStatementsJob_Idempotent_DoubleRunProducesOneStatement()
    {
        var f = await SeedShopAndAuthAsync();
        var (periodStart, _) = LastMonthPeriod();
        var completedAtInPeriod = new DateTimeOffset(periodStart.Year, periodStart.Month, periodStart.Day, 12, 0, 0, TimeSpan.Zero).AddDays(3);
        await CreateBookingAsync(f, BookingStatus.Completed, completedAtInPeriod);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlyCommissionStatementsJob>();
            await job.RunAsync(default);
            await job.RunAsync(default); // çift koşum
        }

        var adminPage = await f.AdminClient.GetJson<AdminStatementsPage>("/v1/admin/billing/statements");
        Assert.Equal(1, adminPage.TotalCount);
    }

    [Fact]
    public async Task CloseMonthlyCommissionStatementsJob_RateApplied_SnapshotsAndLaterGlobalRateChangeDoesNotAffectIt()
    {
        var f = await SeedShopAndAuthAsync();
        await f.AdminClient.PutAsJsonAsync("/v1/admin/billing/settings", new
        {
            commissionRateGlobal = 0.10m,
            commissionPaymentIban = (string?)null,
            commissionPaymentBankName = (string?)null,
            commissionPaymentAccountHolderName = (string?)null,
        });

        var (periodStart, _) = LastMonthPeriod();
        var completedAtInPeriod = new DateTimeOffset(periodStart.Year, periodStart.Month, periodStart.Day, 12, 0, 0, TimeSpan.Zero).AddDays(2);
        await CreateBookingAsync(f, BookingStatus.Completed, completedAtInPeriod);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlyCommissionStatementsJob>();
            await job.RunAsync(default);
        }

        var before = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements")).Items.Single();
        Assert.Equal(0.10m, before.RateApplied);
        var expectedAmount = Kuaforum.Domain.Common.PaymentAmounts.CalculateCommissionMinor(f.Price, 0.10m);
        Assert.Equal(expectedAmount, before.AmountDueMinor);

        // Global oran sonradan değişir.
        await f.AdminClient.PutAsJsonAsync("/v1/admin/billing/settings", new
        {
            commissionRateGlobal = 0.30m,
            commissionPaymentIban = (string?)null,
            commissionPaymentBankName = (string?)null,
            commissionPaymentAccountHolderName = (string?)null,
        });

        var after = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements")).Items.Single();
        Assert.Equal(0.10m, after.RateApplied); // DEĞİŞMEDİ
        Assert.Equal(expectedAmount, after.AmountDueMinor); // DEĞİŞMEDİ
    }

    [Fact]
    public async Task CloseMonthlyCommissionStatementsJob_UsesPerShopOverrideRate_NotGlobal()
    {
        var f = await SeedShopAndAuthAsync();
        await f.AdminClient.PutAsJsonAsync("/v1/admin/billing/settings", new
        {
            commissionRateGlobal = 0.20m,
            commissionPaymentIban = (string?)null,
            commissionPaymentBankName = (string?)null,
            commissionPaymentAccountHolderName = (string?)null,
        });
        var overrideResp = await f.AdminClient.PutAsJsonAsync($"/v1/admin/billing/barber-shops/{f.ShopId}/commission-rate", new { rateOverride = 0.05m });
        Assert.Equal(HttpStatusCode.NoContent, overrideResp.StatusCode);

        var (periodStart, _) = LastMonthPeriod();
        var completedAtInPeriod = new DateTimeOffset(periodStart.Year, periodStart.Month, periodStart.Day, 12, 0, 0, TimeSpan.Zero).AddDays(1);
        await CreateBookingAsync(f, BookingStatus.Completed, completedAtInPeriod);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlyCommissionStatementsJob>();
            await job.RunAsync(default);
        }

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements")).Items.Single();
        Assert.Equal(0.05m, statement.RateApplied); // shop override, global (0.20) değil
    }

    // ── C1: Overdue → Suspend zinciri ──────────────────────────────────

    [Fact]
    public async Task SuspendOverdueCommissionAccountsJob_TransitionsToSuspensionApplied_AndSuspendsShop_ConsistentWithExistingSuspendBehavior()
    {
        var f = await SeedShopAndAuthAsync();
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 100_000, rate: 0.20m, dueDateOverride: yesterday);

        // pre: shop visible in search
        var before = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.Contains(before.Items, s => s.Id == f.ShopId);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueCommissionAccountsJob>();
            await job.RunAsync(default);
        }

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == statementId);
        Assert.Equal(CommissionStatementStatus.SuspensionApplied, statement.Status);

        // Mevcut suspend mekanizmasıyla tutarlı: shop aramadan düşer.
        var after = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.DoesNotContain(after.Items, s => s.Id == f.ShopId);
    }

    [Fact]
    public async Task MarkCommissionStatementPaid_AfterSuspension_DoesNotAutoUnsuspend_AdminMustReactivateSeparately()
    {
        var f = await SeedShopAndAuthAsync();
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 100_000, rate: 0.20m, dueDateOverride: yesterday);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueCommissionAccountsJob>();
            await job.RunAsync(default);
        }

        // admin "ödendi işaretle" (havale onayı)
        var markPaid = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{statementId}/mark-paid",
            new { reason = "Banka dekontu doğrulandı, IBAN ve tutar eşleşti." });
        Assert.Equal(HttpStatusCode.NoContent, markPaid.StatusCode);

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == statementId);
        Assert.Equal(CommissionStatementStatus.Paid, statement.Status);

        // REGRESYON KİLİDİ: shop hâlâ askıda — mark-paid OTOMATİK aktifleştirmedi.
        var stillSuspended = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.DoesNotContain(stillSuspended.Items, s => s.Id == f.ShopId);

        // admin AYRI bir aksiyonla aktifleştirir.
        var reactivate = await f.AdminClient.PostAsync($"/v1/admin/barber-shops/{f.ShopId}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        var reactivated = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.Contains(reactivated.Items, s => s.Id == f.ShopId);
    }

    [Fact]
    public async Task Dispute_DoesNotStopSuspendJob_VadeSayaciDurmaz()
    {
        var f = await SeedShopAndAuthAsync();
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 100_000, rate: 0.20m, dueDateOverride: yesterday);

        var dispute = await f.OwnerClient.PostAsJsonAsync($"/v1/owner/billing/statements/{statementId}/dispute",
            new { note = "Bu ay tutar yanlış hesaplanmış, incelenmesini rica ederim." });
        Assert.Equal(HttpStatusCode.NoContent, dispute.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueCommissionAccountsJob>();
            await job.RunAsync(default);
        }

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == statementId);
        Assert.True(statement.IsDisputed);
        // İtiraz vadeyi DURDURMADI — job normal akışında Suspend'e geçti.
        Assert.Equal(CommissionStatementStatus.SuspensionApplied, statement.Status);
    }

    [Fact]
    public async Task ExtendDueDate_PostponesDueDate_JobNoLongerTreatsItAsOverdue()
    {
        var f = await SeedShopAndAuthAsync();
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 100_000, rate: 0.20m, dueDateOverride: yesterday);

        var newDueDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(10);
        var extend = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{statementId}/extend-due",
            new { newDueDate, reason = "Kuaför dekontu geç yükledi, ek süre tanındı." });
        Assert.Equal(HttpStatusCode.NoContent, extend.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueCommissionAccountsJob>();
            await job.RunAsync(default);
        }

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == statementId);
        Assert.Equal(CommissionStatementStatus.Notified, statement.Status); // Overdue'ya geçmedi
        Assert.Equal(newDueDate, statement.DueDate);
    }

    // ── C2: CommissionPayment iskeleti — Mock provider ─────────────────

    [Fact]
    public async Task CommissionPayment_MockProvider_InitiateThenComplete_MarksStatementPaid()
    {
        var f = await SeedShopAndAuthAsync();
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 200_000, rate: 0.20m);

        var initiateResp = await f.OwnerClient.PostAsync($"/v1/owner/billing/statements/{statementId}/pay/initiate", null);
        Assert.Equal(HttpStatusCode.OK, initiateResp.StatusCode);
        var initiated = (await initiateResp.Content.ReadFromJsonAsync<InitiateCommissionPaymentResp>(TestHelpers.Json))!;
        Assert.Equal(40_000, initiated.AmountMinor); // 200_000 * 0.20
        Assert.NotNull(initiated.ProviderToken);

        var completeResp = await f.OwnerClient.GetAsync($"/v1/owner/billing/commission-payments/mock/complete?token={initiated.ProviderToken}");
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        var statement = (await f.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == statementId);
        Assert.Equal(CommissionStatementStatus.Paid, statement.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<CommissionPayment>().AsNoTracking().FirstAsync(p => p.CommissionStatementId == statementId);
        Assert.Equal(CommissionPaymentStatus.Captured, payment.Status);
        Assert.Equal(PaymentProviderName.Mock, payment.Provider);
    }

    [Fact]
    public async Task CommissionPayment_Initiate_WhenStatementAlreadyPaid_Fails()
    {
        var f = await SeedShopAndAuthAsync();
        var statementId = await CreateNotifiedStatementAsync(f, basisAmountMinor: 100_000, rate: 0.20m);

        var markPaid = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{statementId}/mark-paid",
            new { reason = "Havale ile ödendi, dekont kontrol edildi." });
        Assert.Equal(HttpStatusCode.NoContent, markPaid.StatusCode);

        var initiateResp = await f.OwnerClient.PostAsync($"/v1/owner/billing/statements/{statementId}/pay/initiate", null);
        Assert.False(initiateResp.IsSuccessStatusCode);
    }

    // ── Review fix turu: cross-owner IDOR + Overdue retry ──────────────

    /// <summary>
    /// C1 (review): owner-B, owner-A'nın statement'ına detail/dispute/pay-initiate ile
    /// erişemez — handler guard'ları 403 döner (guard koddaydı ama testsizdi; regresyon kilidi).
    /// </summary>
    [Fact]
    public async Task CrossOwner_CannotAccessOtherOwnersStatement_DetailDisputeAndPayInitiateAllForbidden()
    {
        var ownerA = await SeedShopAndAuthAsync();
        var statementId = await CreateNotifiedStatementAsync(ownerA, basisAmountMinor: 50_000, rate: 0.20m);

        using var ownerBClient = await CreateSecondOwnerWithOwnShopAsync();

        var detail = await ownerBClient.GetAsync($"/v1/owner/billing/statements/{statementId}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);

        var dispute = await ownerBClient.PostAsJsonAsync(
            $"/v1/owner/billing/statements/{statementId}/dispute",
            new { note = "Bu statement bana ait değil ama erişmeyi deniyorum." });
        Assert.Equal(HttpStatusCode.Forbidden, dispute.StatusCode);

        var pay = await ownerBClient.PostAsync($"/v1/owner/billing/statements/{statementId}/pay/initiate", null);
        Assert.Equal(HttpStatusCode.Forbidden, pay.StatusCode);

        // Yan etki yok: statement dispute edilmemiş, durum değişmemiş.
        var fromOwnerA = (await ownerA.OwnerClient.GetJson<OwnerStatementsPage>("/v1/owner/billing/statements"))
            .Items.Single(x => x.Id == statementId);
        Assert.False(fromOwnerA.IsDisputed);
        Assert.Equal(CommissionStatementStatus.Notified, fromOwnerA.Status);
    }

    /// <summary>
    /// I3 (review): önceki koşumda Overdue'ya geçmiş ama suspend edilememiş statement,
    /// job'ın SONRAKİ koşumunda yeniden işlenir (Notified-only seçim bug'ının regresyon kilidi).
    /// </summary>
    [Fact]
    public async Task SuspendJob_RetriesStatementsAlreadyStuckInOverdue()
    {
        var f = await SeedShopAndAuthAsync();
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var statementId = await CreateOverdueStatementDirectlyAsync(f, basisAmountMinor: 80_000, rate: 0.20m, dueDate: yesterday);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueCommissionAccountsJob>();
            await job.RunAsync(default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var statement = await db.Set<CommissionStatement>().AsNoTracking().SingleAsync(x => x.Id == statementId);
            Assert.Equal(CommissionStatementStatus.SuspensionApplied, statement.Status);
            var shop = await db.Set<BarberShop>().AsNoTracking().SingleAsync(x => x.Id == f.ShopId);
            Assert.Equal(BarberShopStatus.Suspended, shop.Status);
        }
    }
}
