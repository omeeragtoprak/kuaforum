using Kuaforum.Domain.Modules.Availability;

namespace Kuaforum.Application.Common.Interfaces;

/// <summary>
/// Cross-module query used by the Availability engine to fetch confirmed/pending
/// bookings that block slots. Implementation in Booking module (M6).
/// Until M6 ships, the stub returns empty list.
/// </summary>
public interface IBusyRangeQuery
{
    Task<IReadOnlyList<BusyRange>> GetBusyRangesAsync(
        Guid stylistId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
