using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Billing.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Billing;

public class SubscriptionInvoiceTests
{
    private static readonly DateOnly PeriodStart = new(2026, 7, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 7, 31);
    private const string PlanCode = "STARTER";

    private static SubscriptionInvoice Create(long planPriceMinor = 100_000, string planCode = PlanCode) =>
        SubscriptionInvoice.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, planCode, planPriceMinor).Value;

    // ── Create / plan fiyatı snapshot ──────────────────────────────────

    [Fact]
    public void Create_HappyPath_AmountMinorEqualsPlanPriceMinor()
    {
        var invoice = Create(planPriceMinor: 149_900);

        Assert.Equal(SubscriptionInvoiceStatus.Open, invoice.Status);
        Assert.Equal("STARTER", invoice.PlanCode);
        Assert.Equal(149_900, invoice.PlanPriceMinor);
        // Tek doğruluk kaynağı SubscriptionPlan.PriceMinor — oran×matrah hesabı yok.
        Assert.Equal(149_900, invoice.AmountMinor);
    }

    [Fact]
    public void Create_PlanCode_NormalizedToUpperInvariant()
    {
        var invoice = Create(planCode: "starter");
        Assert.Equal("STARTER", invoice.PlanCode);
    }

    [Fact]
    public void Create_DueDate_IsPeriodEndPlusSevenDays()
    {
        var invoice = Create();
        Assert.Equal(PeriodEnd.AddDays(7), invoice.DueDate);
    }

    /// <summary>
    /// Fiyat snapshot: fatura kapanış anında Create()'e geçirilen planPriceMinor'ı SAKLAR.
    /// Sonraki bir plan fiyat değişikliği bu fatura'nın AmountMinor'unu geriye dönük ETKİLEMEZ —
    /// çünkü SubscriptionInvoice hiçbir zaman SubscriptionPlan'a canlı referans tutmaz, yalnızca
    /// kapanış anındaki değeri kopyalar.
    /// </summary>
    [Fact]
    public void PlanPriceMinor_SnapshotsAtCreation_LaterPriceChangeDoesNotAffectExistingInvoice()
    {
        var closedWithOldPrice = Create(planPriceMinor: 100_000);
        Assert.Equal(100_000, closedWithOldPrice.PlanPriceMinor);
        Assert.Equal(100_000, closedWithOldPrice.AmountMinor);

        // "Plan fiyatı sonradan değişti" simülasyonu: yeni bir fatura farklı bir fiyatla oluşturulur.
        var closedWithNewPrice = Create(planPriceMinor: 250_000);
        Assert.Equal(250_000, closedWithNewPrice.PlanPriceMinor);

        // Eski fatura DEĞİŞMEDİ.
        Assert.Equal(100_000, closedWithOldPrice.PlanPriceMinor);
        Assert.Equal(100_000, closedWithOldPrice.AmountMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ZeroOrNegativePlanPrice_Fails(long planPriceMinor)
    {
        var r = SubscriptionInvoice.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, PlanCode, planPriceMinor);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Create_EmptyPlanCode_Fails()
    {
        var r = SubscriptionInvoice.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, "", 100_000);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Create_PeriodEndBeforeStart_Fails()
    {
        var r = SubscriptionInvoice.Create(Guid.NewGuid(), PeriodEnd, PeriodStart, PlanCode, 100_000);
        Assert.True(r.IsFailure);
    }

    // ── State machine ────────────────────────────────────────────────

    [Fact]
    public void MarkNotified_FromOpen_TransitionsAndRaisesEvent()
    {
        var invoice = Create();
        var result = invoice.MarkNotified();

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, invoice.Status);
        Assert.Contains(invoice.DomainEvents, e => e is SubscriptionInvoiceNotifiedEvent);
    }

    [Fact]
    public void MarkNotified_FromNonOpen_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();

        var result = invoice.MarkNotified();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkOverdue_FromNotified_Transitions()
    {
        var invoice = Create();
        invoice.MarkNotified();

        var result = invoice.MarkOverdue();
        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Overdue, invoice.Status);
    }

    [Fact]
    public void MarkOverdue_FromOpen_Fails()
    {
        var invoice = Create();
        var result = invoice.MarkOverdue();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ApplySuspension_FromOverdue_TransitionsAndRaisesEvent()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkOverdue();

        var result = invoice.ApplySuspension();
        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.SuspensionApplied, invoice.Status);
        Assert.Contains(invoice.DomainEvents, e => e is SubscriptionInvoiceSuspendedEvent);
    }

    [Fact]
    public void ApplySuspension_FromNotified_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();

        var result = invoice.ApplySuspension();
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(SubscriptionInvoiceStatus.Notified)]
    [InlineData(SubscriptionInvoiceStatus.Overdue)]
    [InlineData(SubscriptionInvoiceStatus.SuspensionApplied)]
    public void MarkPaid_FromPayableStates_Transitions(SubscriptionInvoiceStatus fromStatus)
    {
        var invoice = Create();
        invoice.MarkNotified();
        if (fromStatus is SubscriptionInvoiceStatus.Overdue or SubscriptionInvoiceStatus.SuspensionApplied)
            invoice.MarkOverdue();
        if (fromStatus == SubscriptionInvoiceStatus.SuspensionApplied)
            invoice.ApplySuspension();

        var result = invoice.MarkPaid();
        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void MarkPaid_FromOpen_Fails()
    {
        var invoice = Create();
        var result = invoice.MarkPaid();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkPaid_AlreadyPaid_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkPaid();

        var result = invoice.MarkPaid();
        Assert.True(result.IsFailure);
    }

    // ── Dispute — bayrak, vade sayacını durdurmaz ──────────────────────

    [Fact]
    public void Dispute_SetsFlagWithoutChangingStatusOrDueDate()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkOverdue();
        var dueDateBefore = invoice.DueDate;
        var statusBefore = invoice.Status;

        var result = invoice.Dispute("Bu ay tutar yanlış hesaplanmış.");

        Assert.True(result.IsSuccess);
        Assert.True(invoice.IsDisputed);
        Assert.Equal("Bu ay tutar yanlış hesaplanmış.", invoice.DisputeNote);
        Assert.NotNull(invoice.DisputedAt);
        // İtiraz vadeyi ve durumu DEĞİŞTİRMEZ (RFC-012 kasıtlı karar).
        Assert.Equal(dueDateBefore, invoice.DueDate);
        Assert.Equal(statusBefore, invoice.Status);
    }

    [Fact]
    public void Dispute_WhenPaid_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkPaid();

        var result = invoice.Dispute("İtiraz");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Dispute_EmptyNote_Fails()
    {
        var invoice = Create();
        var result = invoice.Dispute("");
        Assert.True(result.IsFailure);
    }

    // ── Admin adjustments ───────────────────────────────────────────────

    [Fact]
    public void AdjustAmount_UpdatesAmountAndNote_PlanPriceMinorUnchanged()
    {
        var invoice = Create(planPriceMinor: 149_900);
        var result = invoice.AdjustAmount(5_000, "Müşteri iadesi düşüldü, dekont ekte.");

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000, invoice.AmountMinor);
        Assert.Equal(149_900, invoice.PlanPriceMinor); // orijinal plan fiyatı snapshot'ı KORUNUR
        Assert.Equal("Müşteri iadesi düşüldü, dekont ekte.", invoice.AdminAdjustmentNote);
    }

    [Fact]
    public void AdjustAmount_WhenPaid_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkPaid();

        var result = invoice.AdjustAmount(1_000, "Geç düzeltme denemesi.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AdjustAmount_ShortReason_Fails()
    {
        var invoice = Create();
        var result = invoice.AdjustAmount(1_000, "kısa");
        Assert.True(result.IsFailure);
    }

    // Review fix (2026-08-07): 0'a çekilen fatura asla tahsil edilemez ama önceden "geçerli"
    // sayılıyordu (validator GreaterThanOrEqualTo(0) + domain `< 0` guard'ı — ikisi de 0'ı
    // kabul ediyordu). Domain, Application validator'ının tek savunma hattı olamayacağı için
    // burada da reddediyor.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100_00)]
    public void AdjustAmount_ZeroOrNegative_Fails(long newAmountMinor)
    {
        var invoice = Create();
        var result = invoice.AdjustAmount(newAmountMinor, "Yeterince uzun bir gerekçe metni.");
        Assert.True(result.IsFailure);
        Assert.Equal(invoice.PlanPriceMinor, invoice.AmountMinor); // reddedilince tutar DEĞİŞMEMİŞ olmalı
    }

    [Fact]
    public void AdjustAmount_PositiveAmount_Succeeds()
    {
        var invoice = Create();
        var result = invoice.AdjustAmount(1, "Yeterince uzun bir gerekçe metni.");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, invoice.AmountMinor);
    }

    [Fact]
    public void ExtendDueDate_LaterDate_Succeeds()
    {
        var invoice = Create();
        var newDueDate = invoice.DueDate.AddDays(14);

        var result = invoice.ExtendDueDate(newDueDate, "Kuaför tatildeydi, ek süre tanındı.");
        Assert.True(result.IsSuccess);
        Assert.Equal(newDueDate, invoice.DueDate);
    }

    [Fact]
    public void ExtendDueDate_NotLaterThanCurrent_Fails()
    {
        var invoice = Create();
        var result = invoice.ExtendDueDate(invoice.DueDate, "Aynı tarih.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ExtendDueDate_WhenPaid_Fails()
    {
        var invoice = Create();
        invoice.MarkNotified();
        invoice.MarkPaid();

        var result = invoice.ExtendDueDate(invoice.DueDate.AddDays(7), "Geç uzatma denemesi.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ExtendDueDate_OnNotifiedInvoice_StatusStaysNotified()
    {
        var invoice = Create();
        invoice.MarkNotified();
        var newDueDate = invoice.DueDate.AddDays(10);

        var result = invoice.ExtendDueDate(newDueDate, "Notified durumunda uzatma.");

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, invoice.Status);
    }

    /// <summary>
    /// Review fix (RFC-012): "admin uzatması vadeyi durdurabilen tek mekanizma" kararı
    /// Overdue penceresinde de çalışmalı — aksi halde SuspendOverdueSubscriptionsJob'un
    /// Overdue-retry taraması (job doc-comment) hâlâ askı adayı sanmaya devam eder.
    /// </summary>
    [Fact]
    public void ExtendDueDate_OnOverdueInvoice_WithFutureNewDate_RevertsToNotified()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodEnd = today.AddDays(-12); // DueDate = periodEnd+7 = today-5 (geçmiş, gerçekten Overdue)
        var periodStart = periodEnd.AddDays(-30);
        var invoice = SubscriptionInvoice.Create(Guid.NewGuid(), periodStart, periodEnd, PlanCode, 100_000).Value;
        invoice.MarkNotified();
        invoice.MarkOverdue();
        Assert.Equal(SubscriptionInvoiceStatus.Overdue, invoice.Status);

        var newDueDate = today.AddDays(5); // gelecekte
        var result = invoice.ExtendDueDate(newDueDate, "İtiraz inceleniyor, ek süre tanındı.");

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Notified, invoice.Status); // Overdue'dan geri alındı
        Assert.Equal(newDueDate, invoice.DueDate);
    }

    /// <summary>
    /// Yeni vade eski vadeden ileri ama HÂLÂ bugünden geçmişteyse (örn. admin 3 gün uzattı ama
    /// bu hâlâ geçmiş bir tarih) durum bilerek Overdue'da kalır — job bir sonraki koşumda
    /// zaten doğru şekilde yeniden değerlendirir (retry taraması onu görmeye devam eder).
    /// </summary>
    [Fact]
    public void ExtendDueDate_OnOverdueInvoice_WithStillPastNewDate_StaysOverdue()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodEnd = today.AddDays(-20); // DueDate = periodEnd+7 = today-13
        var periodStart = periodEnd.AddDays(-30);
        var invoice = SubscriptionInvoice.Create(Guid.NewGuid(), periodStart, periodEnd, PlanCode, 100_000).Value;
        invoice.MarkNotified();
        invoice.MarkOverdue();

        var newDueDate = invoice.DueDate.AddDays(3); // ileri ama hâlâ geçmişte (today-10)
        Assert.True(newDueDate < today); // testin kendi varsayımının sağlaması

        var result = invoice.ExtendDueDate(newDueDate, "Kısmi uzatma, hâlâ gecikmiş.");

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionInvoiceStatus.Overdue, invoice.Status); // hâlâ Overdue
        Assert.Equal(newDueDate, invoice.DueDate);
    }
}
