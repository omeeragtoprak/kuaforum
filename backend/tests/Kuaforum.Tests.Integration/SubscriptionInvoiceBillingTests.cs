using System.Net;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-014 §2 D1 faturalama tabanı: CommissionBillingTests'in yerini alır. Matrah (ciro × oran)
/// tamamen kalktı — fatura tutarı artık salonun bağlı olduğu SubscriptionPlan.PriceMinor'dır,
/// booking hacminden bağımsız. Fatura → 7 gün vade → hatırlatma → askı → itiraz → ödeme zinciri
/// RFC-012'den AYNEN korunur (bu dosyanın Suspend/Dispute/Payment testleri o davranışın
/// regresyon kilididir, yalnız isimlendirme değişti).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SubscriptionInvoiceBillingTests(IntegrationTestFactory factory) : IAsyncLifetime
{
    // subscription_plans/plan_entitlements ResetDatabaseAsync'in TRUNCATE listesinde YOK
    // (bilinçli — bkz. EntitlementServiceTests XML doc), o yüzden TestPlanCode her testten
    // sonra AÇIKÇA temizlenmeli, aksi halde SubscriptionPlanTests.SubscriptionPlanSeeder_
    // RunTwice_DoesNotDuplicateRows gibi "toplam plan sayısı 3" varsayan testlere sızar.
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // shop_subscriptions FK'i (Restrict) plan satırını silmeden önce temizlenmeli — bu satırlar
        // zaten bir sonraki testin ResetDatabaseAsync'inde TRUNCATE edilecekti, burada erken temizlik.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shop_subscriptions WHERE plan_code = {TestPlanCode}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM subscription_plans WHERE code = {TestPlanCode}");
    }

    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record InvoiceDto(
        Guid Id, DateOnly PeriodStart, DateOnly PeriodEnd, string PlanCode, long PlanPriceMinor,
        long AmountMinor, DateOnly DueDate, SubscriptionInvoiceStatus Status, bool IsDisputed, string? DisputeNote);
    private sealed record OwnerInvoicesPage(IReadOnlyList<InvoiceDto> Items, string? CommissionPaymentIban, string? CommissionPaymentBankName, string? CommissionPaymentAccountHolderName);
    private sealed record AdminInvoiceItem(
        Guid Id, Guid BarberShopId, string BarberShopName, DateOnly PeriodStart, DateOnly PeriodEnd,
        string PlanCode, long PlanPriceMinor, long AmountMinor, DateOnly DueDate,
        SubscriptionInvoiceStatus Status, bool IsDisputed);
    private sealed record AdminInvoicesPage(IReadOnlyList<AdminInvoiceItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record InitiateInvoicePaymentResp(Guid SubscriptionInvoicePaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt, long AmountMinor);
    private sealed record SearchItemDto(Guid Id, string Name);
    private sealed record SearchResp(IReadOnlyList<SearchItemDto> Items, int Page, int PageSize, int TotalCount);

    private const string TestPlanCode = "TEST_PAID_PLAN";
    private const long TestPlanPriceMinor = 149_900;

    private sealed record ShopFixture(HttpClient OwnerClient, HttpClient AdminClient, HttpClient CustomerClient, Guid ShopId);

    // ── Fixture setup ───────────────────────────────────────────────────

    private async Task<ShopFixture> SeedShopAndAuthAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var demoSeeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await demoSeeder.SeedAsync(default);
            var planSeeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.SubscriptionPlanSeeder>();
            await planSeeder.SeedAsync(default); // salonu LAUNCH_FREE'ye bağlar (backfill)

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

        return new ShopFixture(ownerClient, adminClient, customerClient, ownerShop.Id);
    }

    /// <summary>Idempotent test plan — subscription_plans ResetDatabaseAsync'in TRUNCATE listesinde YOK (bkz. EntitlementServiceTests), o yüzden yalnız bir kez oluşturulur.</summary>
    private async Task BindShopToPaidPlanAsync(Guid shopId, string planCode = TestPlanCode, long priceMinor = TestPlanPriceMinor)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await db.Set<SubscriptionPlan>().AnyAsync(p => p.Code == planCode))
        {
            db.Set<SubscriptionPlan>().Add(SubscriptionPlan.Create(planCode, planCode, priceMinor, isActive: true, displayOrder: 50).Value);
            await db.SaveChangesAsync();
        }

        var sub = await db.Set<ShopSubscription>().FirstOrDefaultAsync(s => s.BarberShopId == shopId);
        if (sub is null)
        {
            db.Set<ShopSubscription>().Add(ShopSubscription.Create(shopId, planCode, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddYears(1)).Value);
        }
        else
        {
            sub.ChangePlan(planCode);
        }
        await db.SaveChangesAsync();
    }

    private async Task CancelSubscriptionAsync(Guid shopId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sub = await db.Set<ShopSubscription>().FirstAsync(s => s.BarberShopId == shopId);
        sub.Cancel();
        await db.SaveChangesAsync();
    }

    /// <summary>Doğrudan agregat üretimi + Notified'a taşıma — SuspendOverdueSubscriptionsJob'u yeniden çalıştırmadan Overdue/Suspend/Payment testleri için sabit bir başlangıç durumu kurar.</summary>
    private async Task<Guid> CreateNotifiedInvoiceAsync(ShopFixture f, DateOnly? dueDateOverride = null, string planCode = TestPlanCode, long planPriceMinor = TestPlanPriceMinor)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (periodStart, periodEnd) = LastMonthPeriod();

        var invoice = SubscriptionInvoice.Create(f.ShopId, periodStart, periodEnd, planCode, planPriceMinor).Value;
        invoice.MarkNotified();
        db.Set<SubscriptionInvoice>().Add(invoice);
        await db.SaveChangesAsync();

        if (dueDateOverride.HasValue)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE subscription_invoices SET due_date = {dueDateOverride.Value} WHERE id = {invoice.Id}");
        }

        return invoice.Id;
    }

    private async Task<Guid> CreateOverdueInvoiceDirectlyAsync(ShopFixture f, DateOnly dueDate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (periodStart, periodEnd) = LastMonthPeriod();

        var invoice = SubscriptionInvoice.Create(f.ShopId, periodStart, periodEnd, TestPlanCode, TestPlanPriceMinor).Value;
        invoice.MarkNotified();
        invoice.MarkOverdue();
        db.Set<SubscriptionInvoice>().Add(invoice);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE subscription_invoices SET due_date = {dueDate} WHERE id = {invoice.Id}");
        return invoice.Id;
    }

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

    // ── (a)/(b): kapanış job'u — plan fiyatı tek doğruluk kaynağı ───────

    [Fact]
    public async Task CloseMonthlySubscriptionInvoicesJob_LaunchFreeShop_ProducesNoInvoice()
    {
        var f = await SeedShopAndAuthAsync(); // shop LAUNCH_FREE'de kalır (PriceMinor=0)

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlySubscriptionInvoicesJob>();
            await job.RunAsync(default);
        }

        var page = await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements");
        Assert.Empty(page.Items);

        var adminPage = await f.AdminClient.GetJson<AdminInvoicesPage>("/v1/admin/billing/statements");
        Assert.Equal(0, adminPage.TotalCount);
    }

    [Fact]
    public async Task CloseMonthlySubscriptionInvoicesJob_PaidPlanShop_ProducesInvoiceForPlanPrice()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlySubscriptionInvoicesJob>();
            await job.RunAsync(default);
        }

        var page = await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements");
        var invoice = Assert.Single(page.Items);
        Assert.Equal(TestPlanCode, invoice.PlanCode);
        Assert.Equal(TestPlanPriceMinor, invoice.PlanPriceMinor);
        // Ciro/booking hacminden tamamen bağımsız — bu testte tek bir booking bile
        // oluşturulmadı, yine de fatura tam plan fiyatı kadar üretildi.
        Assert.Equal(TestPlanPriceMinor, invoice.AmountMinor);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, invoice.Status);
        var (periodStart, _) = LastMonthPeriod();
        Assert.Equal(periodStart, invoice.PeriodStart);
    }

    [Fact]
    public async Task CloseMonthlySubscriptionInvoicesJob_Idempotent_DoubleRunProducesOneInvoice()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlySubscriptionInvoicesJob>();
            await job.RunAsync(default);
            await job.RunAsync(default); // çift koşum
        }

        var adminPage = await f.AdminClient.GetJson<AdminInvoicesPage>("/v1/admin/billing/statements");
        Assert.Equal(1, adminPage.TotalCount);
    }

    [Fact]
    public async Task CloseMonthlySubscriptionInvoicesJob_CancelledSubscription_IsSkipped()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        await CancelSubscriptionAsync(f.ShopId);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CloseMonthlySubscriptionInvoicesJob>();
            await job.RunAsync(default);
        }

        var adminPage = await f.AdminClient.GetJson<AdminInvoicesPage>("/v1/admin/billing/statements");
        Assert.Equal(0, adminPage.TotalCount);
    }

    // ── (c): Overdue → Suspend zinciri (RFC-012'den davranış değişmedi) ─

    [Fact]
    public async Task SuspendOverdueSubscriptionsJob_TransitionsToSuspensionApplied_AndSuspendsShop_ConsistentWithExistingSuspendBehavior()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var invoiceId = await CreateNotifiedInvoiceAsync(f, dueDateOverride: yesterday);

        // pre: shop visible in search
        var before = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.Contains(before.Items, s => s.Id == f.ShopId);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueSubscriptionsJob>();
            await job.RunAsync(default);
        }

        var invoice = (await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == invoiceId);
        Assert.Equal(SubscriptionInvoiceStatus.SuspensionApplied, invoice.Status);

        // Mevcut suspend mekanizmasıyla tutarlı: shop aramadan düşer.
        var after = await f.CustomerClient.GetJson<SearchResp>("/v1/barber-shops/search?pageSize=50");
        Assert.DoesNotContain(after.Items, s => s.Id == f.ShopId);
    }

    [Fact]
    public async Task MarkSubscriptionInvoicePaid_AfterSuspension_DoesNotAutoUnsuspend_AdminMustReactivateSeparately()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var invoiceId = await CreateNotifiedInvoiceAsync(f, dueDateOverride: yesterday);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueSubscriptionsJob>();
            await job.RunAsync(default);
        }

        // admin "ödendi işaretle" (havale onayı)
        var markPaid = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{invoiceId}/mark-paid",
            new { reason = "Banka dekontu doğrulandı, IBAN ve tutar eşleşti." });
        Assert.Equal(HttpStatusCode.NoContent, markPaid.StatusCode);

        var invoice = (await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == invoiceId);
        Assert.Equal(SubscriptionInvoiceStatus.Paid, invoice.Status);

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
        await BindShopToPaidPlanAsync(f.ShopId);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var invoiceId = await CreateNotifiedInvoiceAsync(f, dueDateOverride: yesterday);

        var dispute = await f.OwnerClient.PostAsJsonAsync($"/v1/owner/billing/statements/{invoiceId}/dispute",
            new { note = "Bu ay tutar yanlış hesaplanmış, incelenmesini rica ederim." });
        Assert.Equal(HttpStatusCode.NoContent, dispute.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueSubscriptionsJob>();
            await job.RunAsync(default);
        }

        var invoice = (await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == invoiceId);
        Assert.True(invoice.IsDisputed);
        // İtiraz vadeyi DURDURMADI — job normal akışında Suspend'e geçti.
        Assert.Equal(SubscriptionInvoiceStatus.SuspensionApplied, invoice.Status);
    }

    [Fact]
    public async Task ExtendDueDate_PostponesDueDate_JobNoLongerTreatsItAsOverdue()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var invoiceId = await CreateNotifiedInvoiceAsync(f, dueDateOverride: yesterday);

        var newDueDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(10);
        var extend = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{invoiceId}/extend-due",
            new { newDueDate, reason = "Kuaför dekontu geç yükledi, ek süre tanındı." });
        Assert.Equal(HttpStatusCode.NoContent, extend.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueSubscriptionsJob>();
            await job.RunAsync(default);
        }

        var invoice = (await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == invoiceId);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, invoice.Status); // Overdue'ya geçmedi
        Assert.Equal(newDueDate, invoice.DueDate);
    }

    // ── C2: SubscriptionInvoicePayment iskeleti — Mock provider ─────────

    [Fact]
    public async Task SubscriptionInvoicePayment_MockProvider_InitiateThenComplete_MarksInvoicePaid()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var invoiceId = await CreateNotifiedInvoiceAsync(f);

        var initiateResp = await f.OwnerClient.PostAsync($"/v1/owner/billing/statements/{invoiceId}/pay/initiate", null);
        Assert.Equal(HttpStatusCode.OK, initiateResp.StatusCode);
        var initiated = (await initiateResp.Content.ReadFromJsonAsync<InitiateInvoicePaymentResp>(TestHelpers.Json))!;
        Assert.Equal(TestPlanPriceMinor, initiated.AmountMinor);
        Assert.NotNull(initiated.ProviderToken);

        var completeResp = await f.OwnerClient.GetAsync($"/v1/owner/billing/subscription-invoice-payments/mock/complete?token={initiated.ProviderToken}");
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        var invoice = (await f.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(s => s.Id == invoiceId);
        Assert.Equal(SubscriptionInvoiceStatus.Paid, invoice.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<SubscriptionInvoicePayment>().AsNoTracking().FirstAsync(p => p.SubscriptionInvoiceId == invoiceId);
        Assert.Equal(SubscriptionInvoicePaymentStatus.Captured, payment.Status);
        Assert.Equal(PaymentProviderName.Mock, payment.Provider);
    }

    [Fact]
    public async Task SubscriptionInvoicePayment_Initiate_WhenInvoiceAlreadyPaid_Fails()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var invoiceId = await CreateNotifiedInvoiceAsync(f);

        var markPaid = await f.AdminClient.PostAsJsonAsync($"/v1/admin/billing/statements/{invoiceId}/mark-paid",
            new { reason = "Havale ile ödendi, dekont kontrol edildi." });
        Assert.Equal(HttpStatusCode.NoContent, markPaid.StatusCode);

        var initiateResp = await f.OwnerClient.PostAsync($"/v1/owner/billing/statements/{invoiceId}/pay/initiate", null);
        Assert.False(initiateResp.IsSuccessStatusCode);
    }

    // ── (d) komisyon artık kapora/ödeme tutarından düşülmüyor ───────────
    // (Payment.PlatformCommissionMinor'ın DAİMA 0 olduğunun regresyon kilidi
    // PostPaymentApprovalTests.Payment_PlatformCommissionMinor_IsAlwaysZero_RegardlessOfKind'de
    // zaten var — burada yalnız fatura tutarının komisyondan TAMAMEN bağımsız, salt plan fiyatı
    // olduğu ayrıca kanıtlanır: aynı shop'ta booking/ödeme hiç yokken bile fatura tam plan
    // fiyatı kadar üretiliyor (yukarıdaki PaidPlanShop testi zaten booking'siz çalışıyor).

    // ── Review fix turu: cross-owner IDOR + Overdue retry ──────────────

    [Fact]
    public async Task CrossOwner_CannotAccessOtherOwnersInvoice_DetailDisputeAndPayInitiateAllForbidden()
    {
        var ownerA = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(ownerA.ShopId);
        var invoiceId = await CreateNotifiedInvoiceAsync(ownerA);

        using var ownerBClient = await CreateSecondOwnerWithOwnShopAsync();

        var detail = await ownerBClient.GetAsync($"/v1/owner/billing/statements/{invoiceId}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);

        var dispute = await ownerBClient.PostAsJsonAsync(
            $"/v1/owner/billing/statements/{invoiceId}/dispute",
            new { note = "Bu fatura bana ait değil ama erişmeyi deniyorum." });
        Assert.Equal(HttpStatusCode.Forbidden, dispute.StatusCode);

        var pay = await ownerBClient.PostAsync($"/v1/owner/billing/statements/{invoiceId}/pay/initiate", null);
        Assert.Equal(HttpStatusCode.Forbidden, pay.StatusCode);

        // Yan etki yok: fatura dispute edilmemiş, durum değişmemiş.
        var fromOwnerA = (await ownerA.OwnerClient.GetJson<OwnerInvoicesPage>("/v1/owner/billing/statements"))
            .Items.Single(x => x.Id == invoiceId);
        Assert.False(fromOwnerA.IsDisputed);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, fromOwnerA.Status);
    }

    [Fact]
    public async Task SuspendJob_RetriesInvoicesAlreadyStuckInOverdue()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var invoiceId = await CreateOverdueInvoiceDirectlyAsync(f, dueDate: yesterday);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SuspendOverdueSubscriptionsJob>();
            await job.RunAsync(default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var invoice = await db.Set<SubscriptionInvoice>().AsNoTracking().SingleAsync(x => x.Id == invoiceId);
            Assert.Equal(SubscriptionInvoiceStatus.SuspensionApplied, invoice.Status);
            var shop = await db.Set<BarberShop>().AsNoTracking().SingleAsync(x => x.Id == f.ShopId);
            Assert.Equal(BarberShopStatus.Suspended, shop.Status);
        }
    }

    // ── Follow-up (review): admin listesi planCode/planPriceMinor + dönem filtresi ──

    [Fact]
    public async Task GetAdminSubscriptionInvoicesQueue_ReturnsPlanCodeAndPlanPriceMinor()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        await CreateNotifiedInvoiceAsync(f);

        var page = await f.AdminClient.GetJson<AdminInvoicesPage>("/v1/admin/billing/statements");
        var item = Assert.Single(page.Items);
        Assert.Equal(TestPlanCode, item.PlanCode);
        Assert.Equal(TestPlanPriceMinor, item.PlanPriceMinor);
        Assert.Equal(TestPlanPriceMinor, item.AmountMinor);
    }

    [Fact]
    public async Task GetAdminSubscriptionInvoicesQueue_PeriodStartFilter_ReturnsOnlyMatchingPeriod()
    {
        var f = await SeedShopAndAuthAsync();
        await BindShopToPaidPlanAsync(f.ShopId);
        var (lastMonthStart, _) = LastMonthPeriod();
        await CreateNotifiedInvoiceAsync(f);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var twoMonthsAgoStart = new DateOnly(lastMonthStart.Year, lastMonthStart.Month, 1).AddMonths(-1);
            var twoMonthsAgoEnd = lastMonthStart.AddDays(-1);
            var older = SubscriptionInvoice.Create(f.ShopId, twoMonthsAgoStart, twoMonthsAgoEnd, TestPlanCode, TestPlanPriceMinor).Value;
            older.MarkNotified();
            db.Set<SubscriptionInvoice>().Add(older);
            await db.SaveChangesAsync();
        }

        var unfiltered = await f.AdminClient.GetJson<AdminInvoicesPage>("/v1/admin/billing/statements");
        Assert.Equal(2, unfiltered.TotalCount);

        var filtered = await f.AdminClient.GetJson<AdminInvoicesPage>($"/v1/admin/billing/statements?periodStart={lastMonthStart:yyyy-MM-dd}");
        var only = Assert.Single(filtered.Items);
        Assert.Equal(lastMonthStart, only.PeriodStart);
        Assert.Equal(TestPlanPriceMinor, only.PlanPriceMinor);
    }
}
