using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M15_BookingSlotExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            // Concurrent double-booking's guaranteed backstop. The Redis per-stylist lock +
            // busy-range check in InitiateBookingCommand/RescheduleBookingCommand is the fast
            // path; this EXCLUDE constraint is what actually makes overlap impossible at the
            // data layer, independent of app-level races (multiple API instances, lock TTL
            // expiry mid-request, etc).
            //
            // '[)' bound flags → half-open interval, so back-to-back bookings (…10:00) and
            // (10:00…) do NOT count as overlapping — matches the exclusive-end overlap check
            // already used by BookingBusyRangeQuery / the in-app busy-range comparisons.
            //
            // status IN (0,1,2) = Pending, Confirmed, Started (BookingStatus enum, HasConversion<int>)
            // — the same set BookingBusyRangeQuery.BlockingStatuses treats as slot-blocking.
            // Cancelled/Completed/NoShow bookings are excluded so a stylist's history can freely
            // contain past overlapping (rebooked) slots.
            migrationBuilder.Sql("""
                ALTER TABLE bookings
                  ADD CONSTRAINT ex_bookings_stylist_slot
                  EXCLUDE USING gist (
                    stylist_id WITH =,
                    tstzrange(scheduled_at, scheduled_ends_at, '[)') WITH &&
                  )
                  WHERE (status IN (0, 1, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT ex_bookings_stylist_slot;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
