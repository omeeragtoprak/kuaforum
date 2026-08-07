using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Booking.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Booking;

public class BookingTests
{
    private static IReadOnlyList<(Guid, string, int, int, long)> SingleService(long priceMinor = 30000) =>
        new[] { (Guid.NewGuid(), "Saç Kesim", 30, 5, priceMinor) };

    private static Kuaforum.Domain.Modules.Booking.Booking Initiate(
        DateTimeOffset? scheduledAt = null,
        BookingType type = BookingType.Scheduled)
    {
        return Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            customerId: Guid.NewGuid(),
            barberShopId: Guid.NewGuid(),
            stylistId: Guid.NewGuid(),
            scheduledAt: scheduledAt ?? DateTimeOffset.UtcNow.AddDays(7),
            services: SingleService(),
            bookingType: type,
            commissionRate: 0.10m).Value;
    }

    // ── Initiate validation ─────────────────────────────────────────────

    [Fact]
    public void Initiate_HappyPath_RaisesEventAndComputesTotals()
    {
        var booking = Initiate();
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(30000, booking.TotalAmountMinor);
        Assert.Equal(3000, booking.PlatformCommissionMinor); // %10
        Assert.Contains(booking.DomainEvents, e => e is BookingInitiatedEvent);
        Assert.Single(booking.Items);
    }

    [Fact]
    public void Initiate_WithNoServices_Fails()
    {
        var r = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            Array.Empty<(Guid, string, int, int, long)>(),
            BookingType.Scheduled, 0.10m);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Initiate_InPast_Fails()
    {
        var r = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            SingleService(),
            BookingType.Scheduled, 0.10m);
        Assert.True(r.IsFailure);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    public void Initiate_WithCommissionRateOutOfRange_Fails(decimal rate)
    {
        var r = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, rate);
        Assert.True(r.IsFailure);
    }

    // ── State transitions ──────────────────────────────────────────────

    [Fact]
    public void OwnerConfirm_Start_Complete_HappyPath()
    {
        var b = Initiate();
        Assert.True(b.OwnerConfirm().IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.True(b.Start().IsSuccess);
        Assert.Equal(BookingStatus.Started, b.Status);
        Assert.True(b.Complete().IsSuccess);
        Assert.Equal(BookingStatus.Completed, b.Status);
    }

    [Fact]
    public void OwnerConfirm_OnNonPending_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();
        var second = b.OwnerConfirm();
        Assert.True(second.IsFailure);
    }

    [Fact]
    public void Start_OnNonConfirmed_Fails()
    {
        var b = Initiate();
        var r = b.Start();
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Complete_OnNonStarted_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();
        var r = b.Complete();
        Assert.True(r.IsFailure);
    }

    // ── Refund matrix ──────────────────────────────────────────────────

    [Fact]
    public void Cancel_CustomerRequested_MoreThan24h_Returns100Percent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddDays(2));
        b.OwnerConfirm();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(100, b.RefundPercentage);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
    }

    [Fact]
    public void Cancel_CustomerRequested_Between12And24h_Returns50Percent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(18));
        b.OwnerConfirm();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(50, b.RefundPercentage);
    }

    [Fact]
    public void Cancel_CustomerRequested_LessThan12h_ReturnsZeroPercent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(6));
        b.OwnerConfirm();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(0, b.RefundPercentage);
    }

    [Theory]
    [InlineData(BookingCancellationReason.BarberRequested, 100)]
    [InlineData(BookingCancellationReason.PaymentFailed, 100)]
    [InlineData(BookingCancellationReason.PendingTimeout, 100)]
    [InlineData(BookingCancellationReason.AdminOverride, 100)]
    [InlineData(BookingCancellationReason.NoShow, 0)]
    public void Cancel_BarberOrSystemReason_RefundIsFixed(BookingCancellationReason reason, int expectedPct)
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(3)); // intentionally <12h
        b.OwnerConfirm();
        var r = b.Cancel(reason);
        Assert.True(r.IsSuccess);
        Assert.Equal(expectedPct, b.RefundPercentage);
    }

    // ── RFC-013 C2 (adversarial review): onaysız randevuda müşteri iptali ───

    /// <summary>
    /// Owner never called OwnerConfirm() — ConfirmedAt is null. Even though scheduled time is
    /// under 12h away (which would be 0% for a CONFIRMED booking), the customer cancel must be
    /// 100% — an unaccepted request can't keep the customer's money. Reviewer probe: the old
    /// hour-only scale ignored Status entirely and returned 0% here.
    /// </summary>
    [Fact]
    public void Cancel_CustomerRequested_NeverConfirmed_LessThan12h_Returns100Percent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(6)); // <12h, but never confirmed
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(100, b.RefundPercentage);
    }

    [Theory]
    [InlineData(30 * 24)] // far in the future
    [InlineData(18)]      // would be 50% if confirmed
    [InlineData(6)]       // would be 0% if confirmed
    [InlineData(0.1)]     // minutes away, would be 0% if confirmed
    public void Cancel_CustomerRequested_NeverConfirmed_AlwaysReturns100Percent_RegardlessOfTiming(double hoursOut)
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(hoursOut));
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(100, b.RefundPercentage);
    }

    /// <summary>
    /// Regression guard for the same fix: once OwnerConfirm() DID run, the pre-existing hour-based
    /// scale must still apply exactly as before (this is the case the earlier
    /// Cancel_CustomerRequested_* tests already cover with OwnerConfirm() called first — this one
    /// makes the <12h "0%" boundary explicit alongside the never-confirmed 100% case above so the
    /// contrast is obvious in one place).
    /// </summary>
    [Fact]
    public void Cancel_CustomerRequested_Confirmed_LessThan12h_StillReturnsZeroPercent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(6));
        b.OwnerConfirm();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(0, b.RefundPercentage);
    }

    [Fact]
    public void Cancel_AfterCompleted_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();
        b.Start();
        b.Complete();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Cancel_AfterCancelled_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();
        b.Cancel(BookingCancellationReason.CustomerRequested);
        var r = b.Cancel(BookingCancellationReason.AdminOverride);
        Assert.True(r.IsFailure);
    }

    // ── Reschedule + NoShow ────────────────────────────────────────────

    [Fact]
    public void Reschedule_OnConfirmed_PreservesDurationAndUpdatesEndsAt()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddDays(3));
        b.OwnerConfirm();
        var oldDuration = b.ScheduledEndsAt - b.ScheduledAt;
        var newAt = DateTimeOffset.UtcNow.AddDays(5);
        var r = b.Reschedule(newAt);
        Assert.True(r.IsSuccess);
        Assert.Equal(newAt, b.ScheduledAt);
        Assert.Equal(oldDuration, b.ScheduledEndsAt - b.ScheduledAt);
    }

    [Fact]
    public void Reschedule_ToPast_Fails()
    {
        var b = Initiate();
        var r = b.Reschedule(DateTimeOffset.UtcNow.AddDays(-1));
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Reschedule_OnCompleted_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();
        b.Start();
        b.Complete();
        var r = b.Reschedule(DateTimeOffset.UtcNow.AddDays(1));
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void MarkNoShow_BeforeScheduledEnd_Fails()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(2));
        b.OwnerConfirm();
        var r = b.MarkNoShow();
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void MarkNoShow_OnNonConfirmed_Fails()
    {
        var b = Initiate();
        var r = b.MarkNoShow();
        Assert.True(r.IsFailure);
    }

    /// <summary>
    /// Lock-in for the ScheduledEndsAt time-window guard in Booking.MarkNoShow — an owner must
    /// not be able to mark a future appointment as no-show (which would zero out the refund
    /// without the customer ever having a chance to show up). Uses a zero-duration service so
    /// ScheduledEndsAt already elapsed by the time MarkNoShow runs, without needing to wait or
    /// bypass Initiate's "no past bookings" guard (scheduledAt itself is within its -1min grace).
    /// </summary>
    [Fact]
    public void MarkNoShow_AfterScheduledEnd_Succeeds()
    {
        var zeroDurationService = new[] { (Guid.NewGuid(), "Hızlı Kontrol", 0, 0, 5000L) };
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            customerId: Guid.NewGuid(),
            barberShopId: Guid.NewGuid(),
            stylistId: Guid.NewGuid(),
            scheduledAt: DateTimeOffset.UtcNow.AddSeconds(-30),
            services: zeroDurationService,
            bookingType: BookingType.Scheduled,
            commissionRate: 0.10m).Value;
        b.OwnerConfirm();

        var r = b.MarkNoShow();

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(0, b.RefundPercentage);
        Assert.Contains(b.DomainEvents, e => e is BookingNoShowEvent);
    }

    // ── RFC-013 C4: RequiresOnlinePayment snapshot + MarkPaymentReceived ─

    [Fact]
    public void Initiate_DefaultsRequiresOnlinePaymentToFalse()
    {
        var booking = Initiate();
        Assert.False(booking.RequiresOnlinePayment);
        Assert.Null(booking.DepositPaidAt);
    }

    [Fact]
    public void Initiate_WithRequiresOnlinePaymentTrue_SnapshotsIt()
    {
        var booking = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, 0.10m,
            requiresOnlinePayment: true).Value;

        Assert.True(booking.RequiresOnlinePayment);
    }

    [Fact]
    public void MarkPaymentReceived_OnPending_SetsPaymentIdAndDepositPaidAt_WithoutConfirming()
    {
        var b = Initiate();
        var paymentId = Guid.NewGuid();

        var r = b.MarkPaymentReceived(paymentId);

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.Pending, b.Status); // RFC-013 C4: ödeme onaylamıyor
        Assert.Equal(paymentId, b.PaymentId);
        Assert.NotNull(b.DepositPaidAt);
        Assert.Contains(b.DomainEvents, e => e is BookingPaymentReceivedEvent);
        Assert.DoesNotContain(b.DomainEvents, e => e is BookingConfirmedEvent);
    }

    [Fact]
    public void MarkPaymentReceived_CalledTwiceWithSamePaymentId_IsIdempotentNoOp()
    {
        var b = Initiate();
        var paymentId = Guid.NewGuid();
        b.MarkPaymentReceived(paymentId);
        var depositPaidAtAfterFirst = b.DepositPaidAt;

        var second = b.MarkPaymentReceived(paymentId);

        Assert.True(second.IsSuccess);
        Assert.Equal(depositPaidAtAfterFirst, b.DepositPaidAt); // unchanged — no re-stamp
    }

    /// <summary>
    /// The idempotency check (PaymentId equality) must win over the Pending-status guard —
    /// otherwise a legitimate webhook retry arriving AFTER the owner already confirmed the
    /// booking (Status now Confirmed) would incorrectly fail instead of no-op'ing.
    /// </summary>
    [Fact]
    public void MarkPaymentReceived_SamePaymentId_IsIdempotent_EvenAfterOwnerConfirmed()
    {
        var b = Initiate();
        var paymentId = Guid.NewGuid();
        b.MarkPaymentReceived(paymentId);
        b.OwnerConfirm();

        var retry = b.MarkPaymentReceived(paymentId);

        Assert.True(retry.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
    }

    [Fact]
    public void MarkPaymentReceived_OnNonPending_WithDifferentPaymentId_Fails()
    {
        var b = Initiate();
        b.OwnerConfirm();

        var r = b.MarkPaymentReceived(Guid.NewGuid());

        Assert.True(r.IsFailure);
    }

    [Fact]
    public void OwnerConfirm_AfterMarkPaymentReceived_PreservesPaymentIdAndConfirms()
    {
        var b = Initiate();
        var paymentId = Guid.NewGuid();
        b.MarkPaymentReceived(paymentId);

        var r = b.OwnerConfirm();

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(paymentId, b.PaymentId); // MarkPaymentReceived'ın set ettiği değer korunuyor
        Assert.Contains(b.DomainEvents, e => e is BookingConfirmedEvent);
    }

    // ── RFC-014 §2 D2: AutoConfirmEnabled (`auto_confirm`) ────────────────

    [Fact]
    public void Initiate_AutoConfirmDisabled_DefaultsToPending()
    {
        // autoConfirmEnabled hiç verilmezse (default false) — mevcut davranış korunur.
        var b = Initiate();

        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Null(b.ConfirmedAt);
        Assert.False(b.AutoConfirmSnapshot);
        Assert.DoesNotContain(b.DomainEvents, e => e is BookingConfirmedEvent);
    }

    [Fact]
    public void Initiate_AutoConfirmEnabled_NoOnlinePaymentRequired_GoesStraightToConfirmed()
    {
        // Madde 2: auto_confirm açık + kapora/tam ödeme gerekmiyor → Pending atlanır.
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, 0.10m,
            requiresOnlinePayment: false,
            autoConfirmEnabled: true).Value;

        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.NotNull(b.ConfirmedAt);
        Assert.True(b.AutoConfirmSnapshot);
        Assert.Contains(b.DomainEvents, e => e is BookingInitiatedEvent);
        Assert.Contains(b.DomainEvents, e => e is BookingConfirmedEvent { AutoConfirmed: true });
    }

    [Fact]
    public void Initiate_AutoConfirmEnabled_WithOnlinePaymentRequired_StaysPending()
    {
        // Madde 3 (en kritik): auto_confirm açık AMA kapora/tam ödeme gerekiyor → otomatik onay
        // ödemeyi ATLAMAZ, booking Pending başlar — tıpkı auto_confirm kapalıymış gibi.
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, 0.10m,
            requiresOnlinePayment: true,
            autoConfirmEnabled: true).Value;

        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Null(b.ConfirmedAt);
        Assert.True(b.AutoConfirmSnapshot); // ileride MarkPaymentReceived'ın okuyacağı snapshot
        Assert.DoesNotContain(b.DomainEvents, e => e is BookingConfirmedEvent);
    }

    [Fact]
    public void MarkPaymentReceived_AutoConfirmSnapshotTrue_ConfirmsWithoutOwnerAction()
    {
        // Madde 3'ün ikinci yarısı: ödeme tahsil edildiğinde, owner hiç OwnerConfirm() çağırmadan
        // booking Confirmed'a geçer. BookingPaymentReceivedEvent ("onayınızı bekliyor") YERİNE
        // BookingConfirmedEvent(AutoConfirmed: true) yayınlanır — owner'ın aksiyon alması
        // gerekmiyor, "onayınızı bekliyor" bildirimi yanıltıcı olurdu.
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, 0.10m,
            requiresOnlinePayment: true,
            autoConfirmEnabled: true).Value;
        var paymentId = Guid.NewGuid();

        var r = b.MarkPaymentReceived(paymentId);

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.NotNull(b.ConfirmedAt);
        Assert.Equal(paymentId, b.PaymentId);
        Assert.NotNull(b.DepositPaidAt);
        Assert.Contains(b.DomainEvents, e => e is BookingConfirmedEvent { AutoConfirmed: true });
        Assert.DoesNotContain(b.DomainEvents, e => e is BookingPaymentReceivedEvent);
    }

    [Fact]
    public void MarkPaymentReceived_AutoConfirmSnapshotFalse_KeepsExistingBehavior()
    {
        // Regresyon kilidi: auto_confirm kapalıyken hâlâ eski davranış — Pending kalır,
        // BookingPaymentReceivedEvent yayınlanır, owner'ın OwnerConfirm() çağırması gerekir.
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            SingleService(),
            BookingType.Scheduled, 0.10m,
            requiresOnlinePayment: true,
            autoConfirmEnabled: false).Value;

        var r = b.MarkPaymentReceived(Guid.NewGuid());

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Contains(b.DomainEvents, e => e is BookingPaymentReceivedEvent);
        Assert.DoesNotContain(b.DomainEvents, e => e is BookingConfirmedEvent);
    }

    [Fact]
    public void OwnerConfirm_RaisesBookingConfirmedEvent_WithAutoConfirmedFalse()
    {
        // Owner'ın kendi elle onayı her zaman AutoConfirmed: false taşır — bildirim metninin
        // doğru dallanması buna dayanıyor.
        var b = Initiate();

        b.OwnerConfirm();

        Assert.Contains(b.DomainEvents, e => e is BookingConfirmedEvent { AutoConfirmed: false });
    }

    // ── RFC-013 C1 (adversarial review): AttachOrphanPayment — capture booking'in Pending ────
    // ── guard'ını kaybettiği ANDAN SONRA gelirse para yetim kalmasın ─────────────────────────

    [Fact]
    public void AttachOrphanPayment_OnCancelledBooking_SetsPaymentId()
    {
        var b = Initiate();
        b.Cancel(BookingCancellationReason.BarberRequested); // owner reddetti, henüz PaymentId yok
        var paymentId = Guid.NewGuid();

        var r = b.AttachOrphanPayment(paymentId);

        Assert.True(r.IsSuccess);
        Assert.Equal(paymentId, b.PaymentId);
        Assert.Equal(BookingStatus.Cancelled, b.Status); // Status'e dokunmaz
    }

    [Fact]
    public void AttachOrphanPayment_OnNoShowBooking_SetsPaymentId()
    {
        var zeroDurationService = new[] { (Guid.NewGuid(), "Hızlı Kontrol", 0, 0, 5000L) };
        var b = Kuaforum.Domain.Modules.Booking.Booking.Initiate(
            customerId: Guid.NewGuid(), barberShopId: Guid.NewGuid(), stylistId: Guid.NewGuid(),
            scheduledAt: DateTimeOffset.UtcNow.AddSeconds(-30),
            services: zeroDurationService, bookingType: BookingType.Scheduled, commissionRate: 0.10m).Value;
        b.OwnerConfirm();
        b.MarkNoShow();
        var paymentId = Guid.NewGuid();

        var r = b.AttachOrphanPayment(paymentId);

        Assert.True(r.IsSuccess);
        Assert.Equal(paymentId, b.PaymentId);
    }

    [Fact]
    public void AttachOrphanPayment_OnPendingBooking_Fails()
    {
        // Pending'de doğru yol MarkPaymentReceived'dır — AttachOrphanPayment kasıtlı reddeder.
        var b = Initiate();
        var r = b.AttachOrphanPayment(Guid.NewGuid());
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void AttachOrphanPayment_CalledTwiceWithSamePaymentId_IsIdempotentNoOp()
    {
        var b = Initiate();
        b.Cancel(BookingCancellationReason.BarberRequested);
        var paymentId = Guid.NewGuid();
        b.AttachOrphanPayment(paymentId);

        var second = b.AttachOrphanPayment(paymentId);

        Assert.True(second.IsSuccess);
        Assert.Equal(paymentId, b.PaymentId);
    }

    [Fact]
    public void AttachOrphanPayment_DoesNotSetDepositPaidAt()
    {
        // DepositPaidAt yalnız normal/Pending yolunda (MarkPaymentReceived) anlamlı — orphan
        // capture'da bilerek set edilmez (bkz. AttachOrphanPayment XML doc).
        var b = Initiate();
        b.Cancel(BookingCancellationReason.BarberRequested);

        b.AttachOrphanPayment(Guid.NewGuid());

        Assert.Null(b.DepositPaidAt);
    }
}
