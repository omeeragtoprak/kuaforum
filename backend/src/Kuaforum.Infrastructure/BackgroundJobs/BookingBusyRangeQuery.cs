using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Availability;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// Real implementation of <see cref="IBusyRangeQuery"/> backed by the bookings table.
/// Includes Pending + Confirmed + Started bookings as busy ranges.
/// </summary>
public sealed class BookingBusyRangeQuery(IApplicationDbContext db) : IBusyRangeQuery
{
    private static readonly BookingStatus[] BlockingStatuses =
    [
        BookingStatus.Pending,
        BookingStatus.Confirmed,
        BookingStatus.Started,
    ];

    public async Task<IReadOnlyList<BusyRange>> GetBusyRangesAsync(
        Guid stylistId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var ranges = await ctx.Set<Booking>().AsNoTracking()
            .Where(b => b.StylistId == stylistId
                        && BlockingStatuses.Contains(b.Status)
                        && b.ScheduledEndsAt > from
                        && b.ScheduledAt < to)
            .Select(b => new BusyRange(b.ScheduledAt, b.ScheduledEndsAt))
            .ToListAsync(ct);

        return ranges;
    }
}
