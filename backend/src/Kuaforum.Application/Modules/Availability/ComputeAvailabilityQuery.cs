using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Availability;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Availability;

public sealed record ComputeAvailabilityQuery(
    Guid StylistId,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<Guid> ServiceCatalogIds) : IQuery<IReadOnlyList<SlotDto>>;

public sealed record SlotDto(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed class ComputeAvailabilityQueryHandler(
    IApplicationDbContext db,
    IBusyRangeQuery busyRangeQuery)
    : IRequestHandler<ComputeAvailabilityQuery, Result<IReadOnlyList<SlotDto>>>
{
    private static readonly TimeSpan SlotIncrement = TimeSpan.FromMinutes(15);
    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<Result<IReadOnlyList<SlotDto>>> Handle(ComputeAvailabilityQuery request, CancellationToken ct)
    {
        if (request.ToDate < request.FromDate)
            return Result.Failure<IReadOnlyList<SlotDto>>(Error.Validation("Bitiş tarihi başlangıç tarihinden önce olamaz."));

        var rangeDays = request.ToDate.DayNumber - request.FromDate.DayNumber + 1;
        if (rangeDays > 60)
            return Result.Failure<IReadOnlyList<SlotDto>>(Error.Validation("Tarih aralığı en fazla 60 gün olabilir."));

        if (request.ServiceCatalogIds is null || request.ServiceCatalogIds.Count == 0)
            return Result.Failure<IReadOnlyList<SlotDto>>(Error.Validation("En az bir hizmet seçilmeli."));

        var ctx = (DbContext)db;

        var stylist = await ctx.Set<Stylist>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StylistId && s.IsActive, ct);
        if (stylist is null) return Result.Failure<IReadOnlyList<SlotDto>>(Error.NotFound("Stylist"));

        var services = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(s => s.BarberShopId == stylist.BarberShopId
                        && request.ServiceCatalogIds.Contains(s.Id)
                        && s.IsActive)
            .ToListAsync(ct);

        if (services.Count != request.ServiceCatalogIds.Count)
            return Result.Failure<IReadOnlyList<SlotDto>>(Error.Validation("Seçilen hizmetlerin bazıları bulunamadı veya pasif."));

        var totalDuration = services.Sum(s => s.DurationMinutes);
        var totalBuffer = services.Sum(s => s.BufferMinutes);
        var requiredMinutes = totalDuration + totalBuffer;

        if (requiredMinutes <= 0)
            return Result.Failure<IReadOnlyList<SlotDto>>(Error.Validation("Toplam hizmet süresi sıfırdan büyük olmalı."));

        var workingHours = await ctx.Set<WorkingHours>().AsNoTracking()
            .Where(w => w.StylistId == request.StylistId)
            .ToDictionaryAsync(w => w.DayOfWeek, ct);

        var fromUtc = ToUtc(request.FromDate, TimeOnly.MinValue);
        var toUtc = ToUtc(request.ToDate.AddDays(1), TimeOnly.MinValue);

        var timeOffs = await ctx.Set<TimeOff>().AsNoTracking()
            .Where(t => t.StylistId == request.StylistId && t.EndsAt > fromUtc && t.StartsAt < toUtc)
            .Select(t => new BusyRange(t.StartsAt, t.EndsAt))
            .ToListAsync(ct);

        var bookingBusy = await busyRangeQuery.GetBusyRangesAsync(request.StylistId, fromUtc, toUtc, ct);

        var allBusy = timeOffs.Concat(bookingBusy).OrderBy(b => b.StartsAt).ToList();

        var slots = new List<SlotDto>();

        for (var d = request.FromDate; d <= request.ToDate; d = d.AddDays(1))
        {
            if (!workingHours.TryGetValue(d.DayOfWeek, out var wh) || wh.IsClosed)
                continue;

            var dayStart = ToUtc(d, wh.OpenTime);
            var dayEnd = ToUtc(d, wh.CloseTime);

            var cursor = dayStart;
            while (cursor.AddMinutes(requiredMinutes) <= dayEnd)
            {
                var slotEnd = cursor.AddMinutes(requiredMinutes);

                var conflicts = allBusy.Any(b => cursor < b.EndsAt && b.StartsAt < slotEnd);
                if (!conflicts && cursor >= DateTimeOffset.UtcNow)
                    slots.Add(new SlotDto(cursor, slotEnd));

                cursor = cursor.Add(SlotIncrement);
            }
        }

        return Result.Success<IReadOnlyList<SlotDto>>(slots);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, IstanbulTz.GetUtcOffset(local)).ToUniversalTime();
    }
}
