using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Domain.Modules.Payment.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Payment;

public class PaymentTests
{
    private static Kuaforum.Domain.Modules.Payment.Payment Initiate(
        long gross = 30000, long commission = 3000, PaymentProviderName provider = PaymentProviderName.Mock)
    {
        return Kuaforum.Domain.Modules.Payment.Payment.Initiate(
            bookingId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            subMerchantBarberShopId: Guid.NewGuid(),
            grossAmountMinor: gross,
            platformCommissionMinor: commission,
            provider: provider).Value;
    }

    // ── Initiate validation ─────────────────────────────────────────────

    [Fact]
    public void Initiate_HappyPath_DefaultsToInitiated()
    {
        var p = Initiate();
        Assert.Equal(PaymentStatus.Initiated, p.Status);
        Assert.Contains(p.DomainEvents, e => e is PaymentInitiatedEvent);
        Assert.Equal(30000, p.GrossAmountMinor);
        Assert.Equal(3000, p.PlatformCommissionMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Initiate_ZeroOrNegativeGross_Fails(long gross)
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), gross, 0, PaymentProviderName.Mock);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Initiate_CommissionGreaterThanGross_Fails()
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, 200, PaymentProviderName.Mock);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Initiate_NegativeCommission_Fails()
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, -1, PaymentProviderName.Mock);
        Assert.True(r.IsFailure);
    }

    // ── State machine ──────────────────────────────────────────────────

    [Fact]
    public void Capture_OnInitiated_ComputesNetAndRaisesEvent()
    {
        var p = Initiate(gross: 30000, commission: 3000);
        var r = p.Capture("tx-1", pspMdrMinor: 100);
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("tx-1", p.ProviderTransactionId);
        Assert.Equal(100, p.PspMdrMinor);
        Assert.Equal(30000 - 3000 - 100, p.NetToBarberMinor);
        Assert.NotNull(p.CapturedAt);
        Assert.Contains(p.DomainEvents, e => e is PaymentCapturedEvent);
    }

    [Fact]
    public void Capture_OnAuthorized_Succeeds()
    {
        var p = Initiate();
        p.Authorize("tx-auth");
        var r = p.Capture("tx-cap", 50);
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, p.Status);
    }

    [Fact]
    public void Capture_OnAlreadyCaptured_Fails()
    {
        var p = Initiate();
        p.Capture("tx-1", 0);
        var r = p.Capture("tx-2", 0);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Capture_NegativePspMdr_Fails()
    {
        var p = Initiate();
        var r = p.Capture("tx-1", pspMdrMinor: -1);
        Assert.True(r.IsFailure);
        Assert.Equal(PaymentStatus.Initiated, p.Status);
    }

    [Fact]
    public void Authorize_BlankTransactionId_Fails()
    {
        var p = Initiate();
        var r = p.Authorize("   ");
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Authorize_OnNonInitiated_Fails()
    {
        var p = Initiate();
        p.Capture("tx-1", 0);
        var r = p.Authorize("tx-2");
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void MarkSettled_OnCaptured_TransitionsToSettled()
    {
        var p = Initiate();
        p.Capture("tx-1", 0);
        var r = p.MarkSettled();
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentStatus.Settled, p.Status);
        Assert.NotNull(p.SettledAt);
        Assert.Contains(p.DomainEvents, e => e is PaymentSettledEvent);
    }

    [Fact]
    public void MarkSettled_OnInitiated_Fails()
    {
        var p = Initiate();
        var r = p.MarkSettled();
        Assert.True(r.IsFailure);
    }

    // ── Fail path ──────────────────────────────────────────────────────

    [Fact]
    public void Fail_OnInitiated_TransitionsToFailed()
    {
        var p = Initiate();
        var r = p.Fail("3DS reddedildi");
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, p.Status);
        Assert.Equal("3DS reddedildi", p.FailureReason);
        Assert.Contains(p.DomainEvents, e => e is PaymentFailedEvent);
    }

    [Fact]
    public void Fail_OnCaptured_Fails()
    {
        var p = Initiate();
        p.Capture("tx-1", 0);
        var r = p.Fail("PSP geri çağırma");
        Assert.True(r.IsFailure);
    }

    // ── Refund matrix ──────────────────────────────────────────────────

    [Fact]
    public void AddRefund_FullAmount_OnCaptured_Refunded()
    {
        var p = Initiate(gross: 30000, commission: 3000);
        p.Capture("tx-1", 0);
        var refund = p.AddRefund(30000, RefundReason.CustomerRequested);
        Assert.True(refund.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Single(p.Refunds);
    }

    [Fact]
    public void AddRefund_PartialAmount_TransitionsToPartiallyRefunded()
    {
        var p = Initiate(gross: 30000, commission: 3000);
        p.Capture("tx-1", 0);
        var refund = p.AddRefund(15000, RefundReason.CustomerRequested);
        Assert.True(refund.IsSuccess);
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
    }

    [Fact]
    public void AddRefund_AccumulatesUntilFullRefund()
    {
        var p = Initiate(gross: 30000, commission: 3000);
        p.Capture("tx-1", 0);
        p.AddRefund(10000, RefundReason.CustomerRequested);
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        p.AddRefund(20000, RefundReason.CustomerRequested);
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(2, p.Refunds.Count);
    }

    [Fact]
    public void AddRefund_ExceedingRemainingAmount_Fails()
    {
        var p = Initiate(gross: 30000, commission: 3000);
        p.Capture("tx-1", 0);
        p.AddRefund(20000, RefundReason.CustomerRequested);
        var second = p.AddRefund(15000, RefundReason.CustomerRequested); // 20+15 > 30
        Assert.True(second.IsFailure);
    }

    [Fact]
    public void AddRefund_OnInitiated_Fails()
    {
        var p = Initiate();
        var r = p.AddRefund(10000, RefundReason.CustomerRequested);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void AddRefund_ZeroOrNegativeAmount_Fails()
    {
        var p = Initiate();
        p.Capture("tx-1", 0);
        Assert.True(p.AddRefund(0, RefundReason.CustomerRequested).IsFailure);
        Assert.True(p.AddRefund(-10, RefundReason.CustomerRequested).IsFailure);
    }

    // ── PaymentKind (RFC-006 kapora) ────────────────────────────────────

    [Fact]
    public void Initiate_deposit_kind_persists_kind_and_accepts_deposit_at_least_commission()
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            grossAmountMinor: 15_00, platformCommissionMinor: 10_00,
            PaymentProviderName.Mock, kind: PaymentKind.Deposit);
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentKind.Deposit, r.Value.Kind);
    }

    [Fact]
    public void Initiate_deposit_below_commission_fails()
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            grossAmountMinor: 9_00, platformCommissionMinor: 10_00,
            PaymentProviderName.Mock, kind: PaymentKind.Deposit);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Initiate_defaults_to_full_online_kind()
    {
        var r = Kuaforum.Domain.Modules.Payment.Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            grossAmountMinor: 100_00, platformCommissionMinor: 10_00, PaymentProviderName.Mock);
        Assert.True(r.IsSuccess);
        Assert.Equal(PaymentKind.FullOnline, r.Value.Kind);
    }
}
