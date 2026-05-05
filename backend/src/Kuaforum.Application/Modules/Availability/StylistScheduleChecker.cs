using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Availability;

public sealed class StylistScheduleChecker(IApplicationDbContext db) : IStylistScheduleChecker
{
    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<Result> EnsureSlotIsBookableAsync(
        Guid stylistId,
        DateTimeOffset slotStart,
        DateTimeOffset slotEnd,
        CancellationToken ct = default)
    {
        if (slotEnd <= slotStart)
            return Result.Failure(Error.Validation("Randevu bitiş zamanı başlangıç zamanından sonra olmalı."));

        var ctx = (DbContext)db;

        var startLocal = TimeZoneInfo.ConvertTime(slotStart, IstanbulTz);
        var endLocal = TimeZoneInfo.ConvertTime(slotEnd, IstanbulTz);

        if (startLocal.Date != endLocal.Date)
            return Result.Failure(Error.Validation("Randevu tek bir gün içinde başlayıp bitmeli."));

        var dayOfWeek = startLocal.DayOfWeek;
        var wh = await ctx.Set<WorkingHours>().AsNoTracking()
            .FirstOrDefaultAsync(w => w.StylistId == stylistId && w.DayOfWeek == dayOfWeek, ct);

        if (wh is null || wh.IsClosed)
            return Result.Failure(Error.Validation("Stilist seçilen gün için çalışmıyor."));

        var date = DateOnly.FromDateTime(startLocal.DateTime);
        var dayOpenUtc = ToUtc(date, wh.OpenTime);
        var dayCloseUtc = ToUtc(date, wh.CloseTime);

        if (slotStart < dayOpenUtc || slotEnd > dayCloseUtc)
            return Result.Failure(Error.Validation("Seçilen saat stilistin çalışma saatleri dışında."));

        var overlapsTimeOff = await ctx.Set<TimeOff>().AsNoTracking()
            .AnyAsync(t => t.StylistId == stylistId
                           && t.StartsAt < slotEnd
                           && t.EndsAt > slotStart, ct);
        if (overlapsTimeOff)
            return Result.Failure(Error.Conflict("Stilistin bu zaman dilimi için izni var."));

        return Result.Success();
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, IstanbulTz.GetUtcOffset(local)).ToUniversalTime();
    }
}
