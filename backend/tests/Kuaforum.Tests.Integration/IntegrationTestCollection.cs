namespace Kuaforum.Tests.Integration;

// RFC-009: OnlinePaymentsDisabledFactory is a second fixture on the SAME collection (xUnit
// supports multiple ICollectionFixture<T> on one CollectionDefinition) rather than its own
// separate collection. Two collections would run in parallel by default — two extra
// Testcontainers Postgres+Redis pairs alive at once — and empirically destabilized an existing
// timing-sensitive test (BookingSlotSafetyTests.Exclude_constraint_alone_rejects_overlap...
// hit "Sequence contains no elements" reading demo-seed data under the added load). Keeping both
// factories in one collection forces full sequential execution across all integration tests,
// trading a bit of wall-clock time for eliminating that whole class of interference.
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFactory>, ICollectionFixture<OnlinePaymentsDisabledFactory>
{
}
