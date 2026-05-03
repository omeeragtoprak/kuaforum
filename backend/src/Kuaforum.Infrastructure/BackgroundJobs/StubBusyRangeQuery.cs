using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Availability;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// Stub implementation of <see cref="IBusyRangeQuery"/> for use until the
/// Booking module (M6) provides the real implementation backed by appointments table.
/// </summary>
public sealed class StubBusyRangeQuery : IBusyRangeQuery
{
    public Task<IReadOnlyList<BusyRange>> GetBusyRangesAsync(
        Guid stylistId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BusyRange>>([]);
    }
}
