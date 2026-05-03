using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Availability;

public readonly record struct Slot
{
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public TimeSpan Duration => EndsAt - StartsAt;

    private Slot(DateTimeOffset start, DateTimeOffset end)
    {
        StartsAt = start;
        EndsAt = end;
    }

    public static Result<Slot> TryCreate(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            return Result.Failure<Slot>(Error.Validation("End must be after start."));
        return new Slot(start, end);
    }

    public static Slot Unsafe(DateTimeOffset start, DateTimeOffset end) => new(start, end);

    public bool Overlaps(Slot other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => StartsAt < end && start < EndsAt;
    public bool Contains(Slot other) => StartsAt <= other.StartsAt && other.EndsAt <= EndsAt;
}

public readonly record struct BusyRange(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
