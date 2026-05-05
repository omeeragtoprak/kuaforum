using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber;

public sealed class WorkingHours : Entity
{
    private WorkingHours() { }

    public Guid StylistId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public bool IsClosed { get; private set; }

    public static Result<WorkingHours> Create(Guid stylistId, DayOfWeek dow, TimeOnly open, TimeOnly close, bool isClosed = false)
    {
        if (!isClosed && close <= open)
            return Result.Failure<WorkingHours>(Error.Validation("Kapanış saati açılış saatinden sonra olmalı."));

        return new WorkingHours
        {
            StylistId = stylistId,
            DayOfWeek = dow,
            OpenTime = open,
            CloseTime = close,
            IsClosed = isClosed,
        };
    }

    public Result Update(TimeOnly open, TimeOnly close, bool isClosed)
    {
        if (!isClosed && close <= open)
            return Result.Failure(Error.Validation("Kapanış saati açılış saatinden sonra olmalı."));
        OpenTime = open;
        CloseTime = close;
        IsClosed = isClosed;
        Touch();
        return Result.Success();
    }
}

public sealed class TimeOff : Entity
{
    private TimeOff() { }

    public Guid StylistId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Reason { get; private set; }

    public static Result<TimeOff> Create(Guid stylistId, DateTimeOffset start, DateTimeOffset end, string? reason = null)
    {
        if (end <= start)
            return Result.Failure<TimeOff>(Error.Validation("Bitiş zamanı başlangıç zamanından sonra olmalı."));
        if (start < DateTimeOffset.UtcNow.AddDays(-1))
            return Result.Failure<TimeOff>(Error.Validation("Geçmiş tarihli izin oluşturulamaz."));

        return new TimeOff
        {
            StylistId = stylistId,
            StartsAt = start,
            EndsAt = end,
            Reason = reason,
        };
    }
}
