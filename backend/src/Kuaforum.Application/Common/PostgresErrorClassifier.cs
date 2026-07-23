using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Common;

/// <summary>
/// Detects specific PostgreSQL error classes wrapped inside a <see cref="DbUpdateException"/>
/// without taking a hard dependency on Npgsql from the Application layer — reflection-based,
/// mirrors the existing unique-violation check pattern used in InitiatePaymentCommand.
/// </summary>
internal static class PostgresErrorClassifier
{
    private const string UniqueViolationSqlState = "23505";
    private const string ExclusionViolationSqlState = "23P01";

    /// <summary>Unique constraint violation (SQLSTATE 23505) — e.g. duplicate idempotency key.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex, out string? constraintName) =>
        TryGetSqlState(ex, UniqueViolationSqlState, out constraintName);

    /// <summary>
    /// GiST EXCLUDE constraint violation (SQLSTATE 23P01) — raised when two overlapping,
    /// active-status bookings would exist for the same stylist (bookings.ex_bookings_stylist_slot,
    /// see M15_BookingSlotExclusion). This is the last line of defense against double-booking
    /// under concurrency; the Redis slot lock + busy-range check are the fast path, this is the
    /// guaranteed one.
    /// </summary>
    public static bool IsExclusionViolation(DbUpdateException ex, out string? constraintName) =>
        TryGetSqlState(ex, ExclusionViolationSqlState, out constraintName);

    private static bool TryGetSqlState(DbUpdateException ex, string expectedSqlState, out string? constraintName)
    {
        constraintName = null;
        var inner = ex.InnerException;
        if (inner is null) return false;
        var t = inner.GetType();
        if (t.Name != "PostgresException") return false;
        var sqlState = t.GetProperty("SqlState")?.GetValue(inner) as string;
        if (sqlState != expectedSqlState) return false;
        constraintName = t.GetProperty("ConstraintName")?.GetValue(inner) as string;
        return true;
    }
}
