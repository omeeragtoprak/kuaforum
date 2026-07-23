using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking.Events;
using MediatR;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// Wires the refund side-effect into every cancellation path in one place: CancelBookingCommand
/// (customer/owner) and AdminCancelBookingCommand both call Booking.Cancel(), which raises the
/// same BookingCancelledEvent — so a single handler here covers all three actor paths without
/// duplicating "now enqueue a refund" in each command handler (MarkNoShow deliberately raises a
/// different event, BookingNoShowEvent, and isn't touched — no-show is a fixed 0% refund).
///
/// Runs AFTER ApplicationDbContext.SaveChangesAsync's base call has committed the cancellation
/// (domain events are published post-commit — see that override), so by the time this fires the
/// booking's Cancelled/RefundPercentage state is already durable. It only enqueues; it never
/// calls the PSP itself, so a refund failure can never undo the cancellation the customer/owner/
/// admin just made. See ProcessBookingRefundJob for the actual refund attempt + retry.
/// </summary>
public sealed class BookingCancelledRefundHandler(IBookingRefundJobEnqueuer enqueuer)
    : INotificationHandler<BookingCancelledEvent>
{
    public Task Handle(BookingCancelledEvent notification, CancellationToken cancellationToken)
    {
        enqueuer.EnqueueRefund(notification.BookingId);
        return Task.CompletedTask;
    }
}
