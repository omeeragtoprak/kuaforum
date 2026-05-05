using Kuaforum.Domain.Common;

namespace Kuaforum.Application.Common.Interfaces;

/// Validates that a slot fits inside the stylist's working hours and does not
/// overlap with any time-off. Used by Initiate + Reschedule booking handlers.
/// Booking-vs-booking conflicts are handled separately by IBusyRangeQuery.
public interface IStylistScheduleChecker
{
    Task<Result> EnsureSlotIsBookableAsync(
        Guid stylistId,
        DateTimeOffset slotStart,
        DateTimeOffset slotEnd,
        CancellationToken ct = default);
}
