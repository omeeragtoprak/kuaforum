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
    public void Confirm_Start_Complete_HappyPath()
    {
        var b = Initiate();
        Assert.True(b.Confirm(Guid.NewGuid()).IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.True(b.Start().IsSuccess);
        Assert.Equal(BookingStatus.Started, b.Status);
        Assert.True(b.Complete().IsSuccess);
        Assert.Equal(BookingStatus.Completed, b.Status);
    }

    [Fact]
    public void Confirm_OnNonPending_Fails()
    {
        var b = Initiate();
        b.Confirm(Guid.NewGuid());
        var second = b.Confirm(Guid.NewGuid());
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
        b.Confirm(Guid.NewGuid());
        var r = b.Complete();
        Assert.True(r.IsFailure);
    }

    // ── Refund matrix ──────────────────────────────────────────────────

    [Fact]
    public void Cancel_CustomerRequested_MoreThan24h_Returns100Percent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddDays(2));
        b.Confirm(Guid.NewGuid());
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(100, b.RefundPercentage);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
    }

    [Fact]
    public void Cancel_CustomerRequested_Between12And24h_Returns50Percent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(18));
        b.Confirm(Guid.NewGuid());
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsSuccess);
        Assert.Equal(50, b.RefundPercentage);
    }

    [Fact]
    public void Cancel_CustomerRequested_LessThan12h_ReturnsZeroPercent()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(6));
        b.Confirm(Guid.NewGuid());
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
        b.Confirm(Guid.NewGuid());
        var r = b.Cancel(reason);
        Assert.True(r.IsSuccess);
        Assert.Equal(expectedPct, b.RefundPercentage);
    }

    [Fact]
    public void Cancel_AfterCompleted_Fails()
    {
        var b = Initiate();
        b.Confirm(Guid.NewGuid());
        b.Start();
        b.Complete();
        var r = b.Cancel(BookingCancellationReason.CustomerRequested);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Cancel_AfterCancelled_Fails()
    {
        var b = Initiate();
        b.Confirm(Guid.NewGuid());
        b.Cancel(BookingCancellationReason.CustomerRequested);
        var r = b.Cancel(BookingCancellationReason.AdminOverride);
        Assert.True(r.IsFailure);
    }

    // ── Reschedule + NoShow ────────────────────────────────────────────

    [Fact]
    public void Reschedule_OnConfirmed_PreservesDurationAndUpdatesEndsAt()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddDays(3));
        b.Confirm(Guid.NewGuid());
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
        b.Confirm(Guid.NewGuid());
        b.Start();
        b.Complete();
        var r = b.Reschedule(DateTimeOffset.UtcNow.AddDays(1));
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void MarkNoShow_BeforeScheduledEnd_Fails()
    {
        var b = Initiate(DateTimeOffset.UtcNow.AddHours(2));
        b.Confirm(Guid.NewGuid());
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
        b.Confirm(Guid.NewGuid());

        var r = b.MarkNoShow();

        Assert.True(r.IsSuccess);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(0, b.RefundPercentage);
        Assert.Contains(b.DomainEvents, e => e is BookingNoShowEvent);
    }
}
