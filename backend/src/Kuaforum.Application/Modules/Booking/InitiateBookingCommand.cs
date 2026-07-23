using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record InitiateBookingCommand(
    Guid CustomerId,
    Guid StylistId,
    DateTimeOffset ScheduledAt,
    IReadOnlyList<Guid> ServiceCatalogIds,
    bool IsWalkIn,
    string? CustomerNote) : ICommand<InitiateBookingResponse>;

public sealed record InitiateBookingResponse(
    Guid BookingId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt,
    long TotalAmountMinor,
    string Currency);

public sealed class InitiateBookingCommandValidator : AbstractValidator<InitiateBookingCommand>
{
    public InitiateBookingCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.ServiceCatalogIds).NotEmpty();
    }
}

public sealed class InitiateBookingCommandHandler(
    IApplicationDbContext db,
    IDistributedLock distributedLock,
    IBusyRangeQuery busyRangeQuery,
    IStylistScheduleChecker scheduleChecker,
    IAuditLogger audit) : IRequestHandler<InitiateBookingCommand, Result<InitiateBookingResponse>>
{
    private const decimal DefaultCommissionRate = 0.10m;
    private static readonly TimeSpan SlotLockDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Caps concurrent unpaid Pending bookings per customer. Without this, a single logged-in
    /// customer can open unlimited Pending bookings — each blocks a stylist's calendar via
    /// BookingBusyRangeQuery — without ever paying. ExpirePendingBookingsJob eventually frees
    /// stale ones (30 min), but this bounds the damage in the meantime instead of relying only
    /// on the sweep.
    /// </summary>
    internal const int MaxActivePendingBookingsPerCustomer = 3;

    public async Task<Result<InitiateBookingResponse>> Handle(InitiateBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;

        var activePendingCount = await ctx.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.CustomerId == request.CustomerId && b.Status == BookingStatus.Pending, cancellationToken);
        if (activePendingCount >= MaxActivePendingBookingsPerCustomer)
            return Result.Failure<InitiateBookingResponse>(Error.Validation(
                $"En fazla {MaxActivePendingBookingsPerCustomer} bekleyen (ödemesi tamamlanmamış) randevunuz olabilir. " +
                "Devam etmek için bunlardan birini tamamlayın veya iptal edin."));

        var stylist = await ctx.Set<Stylist>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<InitiateBookingResponse>(Error.NotFound("Stylist"));
        if (!stylist.IsActive) return Result.Failure<InitiateBookingResponse>(Error.Validation("Stilist aktif değil."));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.Status != BarberShopStatus.Active)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("İşletme aktif değil."));

        var services = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(s => s.BarberShopId == stylist.BarberShopId
                        && request.ServiceCatalogIds.Contains(s.Id)
                        && s.IsActive)
            .ToListAsync(cancellationToken);

        if (services.Count != request.ServiceCatalogIds.Count)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("Seçilen hizmetlerin bazıları bulunamadı veya aktif değil."));

        var totalDuration = services.Sum(s => s.DurationMinutes + s.BufferMinutes);
        var endsAt = request.ScheduledAt.AddMinutes(totalDuration);

        var scheduleResult = await scheduleChecker.EnsureSlotIsBookableAsync(
            request.StylistId, request.ScheduledAt, endsAt, cancellationToken);
        if (scheduleResult.IsFailure) return Result.Failure<InitiateBookingResponse>(scheduleResult.Error);

        // Locked per-stylist, NOT per-slot-minute: two overlapping-but-not-identical requests
        // (e.g. 10:00-11:00 and 10:30-11:30) would previously take different lock keys and both
        // pass the busy-range check concurrently. Coarser lock, but Initiate is infrequent and
        // short-lived per stylist, so contention is acceptable. The EXCLUDE constraint on
        // bookings (see M15_BookingSlotExclusion) is the actual guarantee — this lock is just
        // the fast, cheap path that avoids hitting it under normal load.
        var lockKey = $"slot-lock:{request.StylistId}";
        await using var lockHandle = await distributedLock.TryAcquireAsync(lockKey, SlotLockDuration, cancellationToken);
        if (lockHandle is null)
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat şu anda başka bir müşteri tarafından rezerve ediliyor. Lütfen tekrar deneyin."));

        var busyRanges = await busyRangeQuery.GetBusyRangesAsync(request.StylistId, request.ScheduledAt, endsAt, cancellationToken);
        if (busyRanges.Any(b => request.ScheduledAt < b.EndsAt && b.StartsAt < endsAt))
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat dolu, başka bir saat seçin."));

        var orderedServices = request.ServiceCatalogIds
            .Select(id => services.First(s => s.Id == id))
            .Select(s => (s.Id, s.Name, s.DurationMinutes, s.BufferMinutes, s.PriceMinor))
            .ToList();

        var bookingResult = BookingAggregate.Initiate(
            request.CustomerId,
            stylist.BarberShopId,
            request.StylistId,
            request.ScheduledAt,
            orderedServices,
            request.IsWalkIn ? BookingType.WalkIn : BookingType.Scheduled,
            DefaultCommissionRate,
            request.CustomerNote);

        if (bookingResult.IsFailure) return Result.Failure<InitiateBookingResponse>(bookingResult.Error);
        var booking = bookingResult.Value;

        ctx.Set<BookingAggregate>().Add(booking);

        await audit.RecordAsync(
            "booking.initiated",
            nameof(BookingAggregate),
            booking.Id.ToString(),
            actorUserId: request.CustomerId,
            payload: new { booking.StylistId, booking.ScheduledAt, booking.TotalAmountMinor, services = request.ServiceCatalogIds },
            ct: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresErrorClassifier.IsExclusionViolation(ex, out _))
        {
            // Last-line-of-defense: the Redis lock + busy-range check above raced past each
            // other (lock held per-stylist but not perfectly serialized against a concurrent
            // reschedule, or the busy-range read happened just before another transaction
            // committed). The DB-level EXCLUDE constraint is what actually guarantees no two
            // active bookings overlap for the same stylist.
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat az önce doldu, başka bir saat seçin."));
        }

        return Result.Success(new InitiateBookingResponse(
            booking.Id, booking.ScheduledAt, booking.ScheduledEndsAt, booking.TotalAmountMinor, booking.Currency));
    }
}
