using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record RescheduleBookingCommand(
    Guid ActorUserId,
    Guid BookingId,
    DateTimeOffset NewScheduledAt) : ICommand<RescheduleBookingResponse>;

public sealed record RescheduleBookingResponse(
    Guid BookingId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt);

public sealed class RescheduleBookingCommandValidator : AbstractValidator<RescheduleBookingCommand>
{
    public RescheduleBookingCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.NewScheduledAt)
            .GreaterThan(DateTimeOffset.UtcNow.AddMinutes(5))
            .WithMessage("Yeni randevu zamanı en az 5 dakika sonra olmalı.");
    }
}

public sealed class RescheduleBookingCommandHandler(
    IApplicationDbContext db,
    IDistributedLock distributedLock,
    IBusyRangeQuery busyRangeQuery,
    IStylistScheduleChecker scheduleChecker,
    IAuditLogger audit) : IRequestHandler<RescheduleBookingCommand, Result<RescheduleBookingResponse>>
{
    private static readonly TimeSpan SlotLockDuration = TimeSpan.FromMinutes(2);

    public async Task<Result<RescheduleBookingResponse>> Handle(RescheduleBookingCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, ct);
        if (booking is null) return Result.Failure<RescheduleBookingResponse>(Error.NotFound("Booking"));

        // Authorization: customer who owns booking, OR shop owner
        var isCustomer = booking.CustomerId == request.ActorUserId;
        var isOwner = false;
        if (!isCustomer)
        {
            var shop = await ctx.Set<BarberShop>().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, ct);
            isOwner = shop is not null && shop.OwnerId == request.ActorUserId;
        }
        if (!isCustomer && !isOwner)
            return Result.Failure<RescheduleBookingResponse>(Error.Forbidden("Bu randevuyu yeniden planlama yetkiniz yok."));

        if (isCustomer && !isOwner)
        {
            var hoursUntilOriginal = (booking.ScheduledAt - DateTimeOffset.UtcNow).TotalHours;
            if (hoursUntilOriginal < 2)
                return Result.Failure<RescheduleBookingResponse>(Error.Validation(
                    "Randevuya 2 saatten az kaldığında müşteri olarak yeniden planlayamazsın. Lütfen kuaförle iletişime geç veya randevuyu iptal et."));
        }

        var totalDuration = booking.ScheduledEndsAt - booking.ScheduledAt;
        var newEndsAt = request.NewScheduledAt.Add(totalDuration);

        var scheduleResult = await scheduleChecker.EnsureSlotIsBookableAsync(
            booking.StylistId, request.NewScheduledAt, newEndsAt, ct);
        if (scheduleResult.IsFailure) return Result.Failure<RescheduleBookingResponse>(scheduleResult.Error);

        // Slot lock + busy-range check on the new time (skip current booking's own range).
        // Locked per-stylist, NOT per-slot-minute — see InitiateBookingCommand for why. The
        // EXCLUDE constraint on bookings (M15_BookingSlotExclusion) is the actual guarantee.
        var lockKey = $"slot-lock:{booking.StylistId}";
        await using var lockHandle = await distributedLock.TryAcquireAsync(lockKey, SlotLockDuration, ct);
        if (lockHandle is null)
            return Result.Failure<RescheduleBookingResponse>(Error.Conflict("Bu zaman dilimi başka bir müşteri tarafından rezerve ediliyor."));

        // Note: BusyRange exposes only Start/End — current booking's existing range is excluded
        // by passing current ID through the query implementation, OR we accept a small false-positive
        // when new and old ranges overlap (rare; user reschedules to a clearly different time).
        var busy = await busyRangeQuery.GetBusyRangesAsync(booking.StylistId, request.NewScheduledAt, newEndsAt, ct);
        var overlapping = busy.Any(b =>
            request.NewScheduledAt < b.EndsAt && b.StartsAt < newEndsAt
            && !(b.StartsAt == booking.ScheduledAt && b.EndsAt == booking.ScheduledEndsAt));
        if (overlapping)
            return Result.Failure<RescheduleBookingResponse>(Error.Conflict("Seçtiğiniz zaman dilimi dolu."));

        var result = booking.Reschedule(request.NewScheduledAt);
        if (result.IsFailure) return Result.Failure<RescheduleBookingResponse>(result.Error);

        await audit.RecordAsync("booking.rescheduled", nameof(BookingAggregate), booking.Id.ToString(),
            actorUserId: request.ActorUserId,
            payload: new { newScheduledAt = request.NewScheduledAt },
            ct: ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrorClassifier.IsExclusionViolation(ex, out _))
        {
            // Last-line-of-defense — see InitiateBookingCommand's identical catch for rationale.
            return Result.Failure<RescheduleBookingResponse>(Error.Conflict("Bu saat az önce doldu, başka bir saat seçin."));
        }

        return Result.Success(new RescheduleBookingResponse(
            booking.Id, booking.ScheduledAt, booking.ScheduledEndsAt));
    }
}
