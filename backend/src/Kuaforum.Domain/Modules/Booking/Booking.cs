using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Booking.Events;

namespace Kuaforum.Domain.Modules.Booking;

public sealed class Booking : AggregateRoot
{
    private readonly List<BookingItem> _items = new();

    private Booking() { }

    public Guid CustomerId { get; private set; }
    public Guid BarberShopId { get; private set; }
    public Guid StylistId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset ScheduledEndsAt { get; private set; }
    public BookingStatus Status { get; private set; }
    public BookingType BookingType { get; private set; }
    public long TotalAmountMinor { get; private set; }
    public long PlatformCommissionMinor { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? CustomerNote { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public BookingCancellationReason? CancellationReason { get; private set; }
    public int? RefundPercentage { get; private set; }
    public Guid? PaymentId { get; private set; }

    public IReadOnlyCollection<BookingItem> Items => _items.AsReadOnly();

    public static Result<Booking> Initiate(
        Guid customerId,
        Guid barberShopId,
        Guid stylistId,
        DateTimeOffset scheduledAt,
        IReadOnlyList<(Guid ServiceId, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor)> services,
        BookingType bookingType,
        decimal commissionRate,
        string? customerNote = null)
    {
        if (services.Count == 0)
            return Result.Failure<Booking>(Error.Validation("At least one service is required."));
        if (scheduledAt < DateTimeOffset.UtcNow.AddMinutes(-1))
            return Result.Failure<Booking>(Error.Validation("Cannot book in the past."));
        if (commissionRate is < 0 or > 0.5m)
            return Result.Failure<Booking>(Error.Validation("Commission rate must be between 0 and 50%."));

        var totalDuration = services.Sum(s => s.DurationMinutes + s.BufferMinutes);
        var totalAmount = services.Sum(s => s.PriceMinor);
        var commission = (long)(totalAmount * commissionRate);

        var booking = new Booking
        {
            CustomerId = customerId,
            BarberShopId = barberShopId,
            StylistId = stylistId,
            ScheduledAt = scheduledAt,
            ScheduledEndsAt = scheduledAt.AddMinutes(totalDuration),
            Status = BookingStatus.Pending,
            BookingType = bookingType,
            TotalAmountMinor = totalAmount,
            PlatformCommissionMinor = commission,
            CustomerNote = customerNote?.Length > 500 ? customerNote[..500] : customerNote,
        };

        var orderIndex = 0;
        foreach (var s in services)
        {
            booking._items.Add(BookingItem.Create(booking.Id, s.ServiceId, s.Name, s.DurationMinutes, s.BufferMinutes, s.PriceMinor, orderIndex++));
        }

        booking.RaiseDomainEvent(new BookingInitiatedEvent(
            booking.Id, customerId, barberShopId, stylistId, scheduledAt, totalAmount));

        return booking;
    }

    public Result Confirm(Guid paymentId)
    {
        if (Status != BookingStatus.Pending)
            return Result.Failure(Error.Validation($"Cannot confirm booking in status {Status}."));

        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        PaymentId = paymentId;
        Touch();

        RaiseDomainEvent(new BookingConfirmedEvent(Id, CustomerId, StylistId, ScheduledAt));
        return Result.Success();
    }

    public Result Start()
    {
        if (Status != BookingStatus.Confirmed)
            return Result.Failure(Error.Validation($"Cannot start booking in status {Status}."));

        Status = BookingStatus.Started;
        StartedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingStartedEvent(Id, StartedAt.Value));
        return Result.Success();
    }

    public Result Complete()
    {
        if (Status != BookingStatus.Started)
            return Result.Failure(Error.Validation($"Cannot complete booking in status {Status}."));

        Status = BookingStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingCompletedEvent(Id, CustomerId, StylistId, CompletedAt.Value));
        return Result.Success();
    }

    public Result Cancel(BookingCancellationReason reason)
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
            return Result.Failure(Error.Validation($"Cannot cancel booking in status {Status}."));

        var refundPct = ComputeRefundPercentage(reason);

        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        RefundPercentage = refundPct;
        Touch();

        RaiseDomainEvent(new BookingCancelledEvent(Id, reason, refundPct));
        return Result.Success();
    }

    public Result MarkNoShow()
    {
        if (Status != BookingStatus.Confirmed)
            return Result.Failure(Error.Validation($"Cannot mark no-show in status {Status}."));
        if (DateTimeOffset.UtcNow < ScheduledEndsAt)
            return Result.Failure(Error.Validation("Cannot mark no-show before booking end time."));

        Status = BookingStatus.NoShow;
        CancellationReason = BookingCancellationReason.NoShow;
        RefundPercentage = 0;
        Touch();

        RaiseDomainEvent(new BookingNoShowEvent(Id, CustomerId));
        return Result.Success();
    }

    private int ComputeRefundPercentage(BookingCancellationReason reason)
    {
        if (reason == BookingCancellationReason.BarberRequested) return 100;
        if (reason == BookingCancellationReason.PaymentFailed) return 100;
        if (reason == BookingCancellationReason.PendingTimeout) return 100;
        if (reason == BookingCancellationReason.AdminOverride) return 100;
        if (reason == BookingCancellationReason.NoShow) return 0;

        var hoursUntil = (ScheduledAt - DateTimeOffset.UtcNow).TotalHours;
        if (hoursUntil >= 24) return 100;
        if (hoursUntil >= 12) return 50;
        return 0;
    }
}

public sealed class BookingItem : Entity
{
    private BookingItem() { }

    public Guid BookingId { get; private set; }
    public Guid ServiceCatalogId { get; private set; }
    public string ServiceName { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public int BufferMinutes { get; private set; }
    public long PriceMinor { get; private set; }
    public int OrderIndex { get; private set; }

    public static BookingItem Create(Guid bookingId, Guid serviceCatalogId, string name, int duration, int buffer, long price, int orderIndex)
    {
        return new BookingItem
        {
            BookingId = bookingId,
            ServiceCatalogId = serviceCatalogId,
            ServiceName = name,
            DurationMinutes = duration,
            BufferMinutes = buffer,
            PriceMinor = price,
            OrderIndex = orderIndex,
        };
    }
}
