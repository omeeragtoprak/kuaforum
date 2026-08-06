using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Billing.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Billing;

public class CommissionStatementTests
{
    private static readonly DateOnly PeriodStart = new(2026, 7, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 7, 31);

    private static CommissionStatement Create(long basis = 100_000, decimal rate = 0.20m) =>
        CommissionStatement.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, basis, rate).Value;

    // ── Create / matrah + oran snapshot ────────────────────────────────

    [Fact]
    public void Create_HappyPath_ComputesAmountDueViaPaymentAmounts()
    {
        var statement = Create(basis: 100_000, rate: 0.20m);

        Assert.Equal(CommissionStatementStatus.Open, statement.Status);
        Assert.Equal(100_000, statement.BasisAmountMinor);
        Assert.Equal(0.20m, statement.RateApplied);
        // Tek doğruluk kaynağı PaymentAmounts.CalculateCommissionMinor ile birebir.
        Assert.Equal(PaymentAmounts.CalculateCommissionMinor(100_000, 0.20m), statement.AmountDueMinor);
    }

    [Fact]
    public void Create_DueDate_IsPeriodEndPlusSevenDays()
    {
        var statement = Create();
        Assert.Equal(PeriodEnd.AddDays(7), statement.DueDate);
    }

    /// <summary>
    /// Oran snapshot: statement kapanış anında Create()'e geçirilen rateApplied'ı SAKLAR.
    /// Sonraki bir global oran değişikliği (PlatformSettings.CommissionRateGlobal) bu
    /// statement'ın AmountDueMinor'unu geriye dönük ETKİLEMEZ — çünkü CommissionStatement
    /// hiçbir zaman PlatformSettings'e canlı referans tutmaz, yalnızca kapanış anındaki
    /// değeri kopyalar. Bu test, iki farklı oranla iki statement oluşturup ikisinin de
    /// kendi kapanış anındaki oranı koruduğunu — birbirini etkilemediğini kanıtlar.
    /// </summary>
    [Fact]
    public void RateApplied_SnapshotsAtCreation_LaterRateChangeDoesNotAffectExistingStatement()
    {
        var closedWithOldRate = Create(basis: 100_000, rate: 0.10m);
        Assert.Equal(0.10m, closedWithOldRate.RateApplied);
        Assert.Equal(PaymentAmounts.CalculateCommissionMinor(100_000, 0.10m), closedWithOldRate.AmountDueMinor);

        // "Global oran sonradan değişti" simülasyonu: yeni bir statement farklı bir rate ile
        // oluşturulur (gerçek job'da PlatformSettings.CommissionRateGlobal okunup Create'e geçirilir).
        var closedWithNewRate = Create(basis: 100_000, rate: 0.25m);
        Assert.Equal(0.25m, closedWithNewRate.RateApplied);

        // Eski statement DEĞİŞMEDİ.
        Assert.Equal(0.10m, closedWithOldRate.RateApplied);
        Assert.Equal(PaymentAmounts.CalculateCommissionMinor(100_000, 0.10m), closedWithOldRate.AmountDueMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ZeroOrNegativeBasis_Fails(long basis)
    {
        var r = CommissionStatement.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, basis, 0.20m);
        Assert.True(r.IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    public void Create_InvalidRate_Fails(double rate)
    {
        var r = CommissionStatement.Create(Guid.NewGuid(), PeriodStart, PeriodEnd, 100_000, (decimal)rate);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Create_PeriodEndBeforeStart_Fails()
    {
        var r = CommissionStatement.Create(Guid.NewGuid(), PeriodEnd, PeriodStart, 100_000, 0.20m);
        Assert.True(r.IsFailure);
    }

    // ── State machine ────────────────────────────────────────────────

    [Fact]
    public void MarkNotified_FromOpen_TransitionsAndRaisesEvent()
    {
        var statement = Create();
        var result = statement.MarkNotified();

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Notified, statement.Status);
        Assert.Contains(statement.DomainEvents, e => e is CommissionStatementNotifiedEvent);
    }

    [Fact]
    public void MarkNotified_FromNonOpen_Fails()
    {
        var statement = Create();
        statement.MarkNotified();

        var result = statement.MarkNotified();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkOverdue_FromNotified_Transitions()
    {
        var statement = Create();
        statement.MarkNotified();

        var result = statement.MarkOverdue();
        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Overdue, statement.Status);
    }

    [Fact]
    public void MarkOverdue_FromOpen_Fails()
    {
        var statement = Create();
        var result = statement.MarkOverdue();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ApplySuspension_FromOverdue_TransitionsAndRaisesEvent()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkOverdue();

        var result = statement.ApplySuspension();
        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.SuspensionApplied, statement.Status);
        Assert.Contains(statement.DomainEvents, e => e is CommissionStatementSuspendedEvent);
    }

    [Fact]
    public void ApplySuspension_FromNotified_Fails()
    {
        var statement = Create();
        statement.MarkNotified();

        var result = statement.ApplySuspension();
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(CommissionStatementStatus.Notified)]
    [InlineData(CommissionStatementStatus.Overdue)]
    [InlineData(CommissionStatementStatus.SuspensionApplied)]
    public void MarkPaid_FromPayableStates_Transitions(CommissionStatementStatus fromStatus)
    {
        var statement = Create();
        statement.MarkNotified();
        if (fromStatus is CommissionStatementStatus.Overdue or CommissionStatementStatus.SuspensionApplied)
            statement.MarkOverdue();
        if (fromStatus == CommissionStatementStatus.SuspensionApplied)
            statement.ApplySuspension();

        var result = statement.MarkPaid();
        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Paid, statement.Status);
    }

    [Fact]
    public void MarkPaid_FromOpen_Fails()
    {
        var statement = Create();
        var result = statement.MarkPaid();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkPaid_AlreadyPaid_Fails()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkPaid();

        var result = statement.MarkPaid();
        Assert.True(result.IsFailure);
    }

    // ── Dispute — bayrak, vade sayacını durdurmaz ──────────────────────

    [Fact]
    public void Dispute_SetsFlagWithoutChangingStatusOrDueDate()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkOverdue();
        var dueDateBefore = statement.DueDate;
        var statusBefore = statement.Status;

        var result = statement.Dispute("Bu ay tutar yanlış hesaplanmış.");

        Assert.True(result.IsSuccess);
        Assert.True(statement.IsDisputed);
        Assert.Equal("Bu ay tutar yanlış hesaplanmış.", statement.DisputeNote);
        Assert.NotNull(statement.DisputedAt);
        // İtiraz vadeyi ve durumu DEĞİŞTİRMEZ (RFC-012 kasıtlı karar).
        Assert.Equal(dueDateBefore, statement.DueDate);
        Assert.Equal(statusBefore, statement.Status);
    }

    [Fact]
    public void Dispute_WhenPaid_Fails()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkPaid();

        var result = statement.Dispute("İtiraz");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Dispute_EmptyNote_Fails()
    {
        var statement = Create();
        var result = statement.Dispute("");
        Assert.True(result.IsFailure);
    }

    // ── Admin adjustments ───────────────────────────────────────────────

    [Fact]
    public void AdjustAmount_UpdatesAmountAndNote()
    {
        var statement = Create();
        var result = statement.AdjustAmount(5_000, "Müşteri iadesi düşüldü, dekont ekte.");

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000, statement.AmountDueMinor);
        Assert.Equal("Müşteri iadesi düşüldü, dekont ekte.", statement.AdminAdjustmentNote);
    }

    [Fact]
    public void AdjustAmount_WhenPaid_Fails()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkPaid();

        var result = statement.AdjustAmount(1_000, "Geç düzeltme denemesi.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AdjustAmount_ShortReason_Fails()
    {
        var statement = Create();
        var result = statement.AdjustAmount(1_000, "kısa");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ExtendDueDate_LaterDate_Succeeds()
    {
        var statement = Create();
        var newDueDate = statement.DueDate.AddDays(14);

        var result = statement.ExtendDueDate(newDueDate, "Kuaför tatildeydi, ek süre tanındı.");
        Assert.True(result.IsSuccess);
        Assert.Equal(newDueDate, statement.DueDate);
    }

    [Fact]
    public void ExtendDueDate_NotLaterThanCurrent_Fails()
    {
        var statement = Create();
        var result = statement.ExtendDueDate(statement.DueDate, "Aynı tarih.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ExtendDueDate_WhenPaid_Fails()
    {
        var statement = Create();
        statement.MarkNotified();
        statement.MarkPaid();

        var result = statement.ExtendDueDate(statement.DueDate.AddDays(7), "Geç uzatma denemesi.");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ExtendDueDate_OnNotifiedStatement_StatusStaysNotified()
    {
        var statement = Create();
        statement.MarkNotified();
        var newDueDate = statement.DueDate.AddDays(10);

        var result = statement.ExtendDueDate(newDueDate, "Notified durumunda uzatma.");

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Notified, statement.Status);
    }

    /// <summary>
    /// Review fix (RFC-012): "admin uzatması vadeyi durdurabilen tek mekanizma" kararı
    /// Overdue penceresinde de çalışmalı — aksi halde SuspendOverdueCommissionAccountsJob'un
    /// Overdue-retry taraması (job doc-comment) hâlâ askı adayı sanmaya devam eder.
    /// </summary>
    [Fact]
    public void ExtendDueDate_OnOverdueStatement_WithFutureNewDate_RevertsToNotified()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodEnd = today.AddDays(-12); // DueDate = periodEnd+7 = today-5 (geçmiş, gerçekten Overdue)
        var periodStart = periodEnd.AddDays(-30);
        var statement = CommissionStatement.Create(Guid.NewGuid(), periodStart, periodEnd, 100_000, 0.20m).Value;
        statement.MarkNotified();
        statement.MarkOverdue();
        Assert.Equal(CommissionStatementStatus.Overdue, statement.Status);

        var newDueDate = today.AddDays(5); // gelecekte
        var result = statement.ExtendDueDate(newDueDate, "İtiraz inceleniyor, ek süre tanındı.");

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Notified, statement.Status); // Overdue'dan geri alındı
        Assert.Equal(newDueDate, statement.DueDate);
    }

    /// <summary>
    /// Yeni vade eski vadeden ileri ama HÂLÂ bugünden geçmişteyse (örn. admin 3 gün uzattı ama
    /// bu hâlâ geçmiş bir tarih) durum bilerek Overdue'da kalır — job bir sonraki koşumda
    /// zaten doğru şekilde yeniden değerlendirir (retry taraması onu görmeye devam eder).
    /// </summary>
    [Fact]
    public void ExtendDueDate_OnOverdueStatement_WithStillPastNewDate_StaysOverdue()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodEnd = today.AddDays(-20); // DueDate = periodEnd+7 = today-13
        var periodStart = periodEnd.AddDays(-30);
        var statement = CommissionStatement.Create(Guid.NewGuid(), periodStart, periodEnd, 100_000, 0.20m).Value;
        statement.MarkNotified();
        statement.MarkOverdue();

        var newDueDate = statement.DueDate.AddDays(3); // ileri ama hâlâ geçmişte (today-10)
        Assert.True(newDueDate < today); // testin kendi varsayımının sağlaması

        var result = statement.ExtendDueDate(newDueDate, "Kısmi uzatma, hâlâ gecikmiş.");

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissionStatementStatus.Overdue, statement.Status); // hâlâ Overdue
        Assert.Equal(newDueDate, statement.DueDate);
    }
}
