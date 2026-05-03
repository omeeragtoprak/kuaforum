using FluentValidation;
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
    IAuditLogger audit) : IRequestHandler<InitiateBookingCommand, Result<InitiateBookingResponse>>
{
    private const decimal DefaultCommissionRate = 0.10m;
    private static readonly TimeSpan SlotLockDuration = TimeSpan.FromMinutes(2);

    public async Task<Result<InitiateBookingResponse>> Handle(InitiateBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<InitiateBookingResponse>(Error.NotFound("Stylist"));
        if (!stylist.IsActive) return Result.Failure<InitiateBookingResponse>(Error.Validation("Stylist is inactive."));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.Status != BarberShopStatus.Active)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("Barber shop is not active."));

        var services = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(s => s.BarberShopId == stylist.BarberShopId
                        && request.ServiceCatalogIds.Contains(s.Id)
                        && s.IsActive)
            .ToListAsync(cancellationToken);

        if (services.Count != request.ServiceCatalogIds.Count)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("Some services are missing or inactive."));

        var totalDuration = services.Sum(s => s.DurationMinutes + s.BufferMinutes);
        var endsAt = request.ScheduledAt.AddMinutes(totalDuration);

        var lockKey = $"slot-lock:{request.StylistId}:{request.ScheduledAt:yyyyMMddHHmm}";
        await using var lockHandle = await distributedLock.TryAcquireAsync(lockKey, SlotLockDuration, cancellationToken);
        if (lockHandle is null)
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Slot is currently being reserved by another customer. Please retry."));

        var busyRanges = await busyRangeQuery.GetBusyRangesAsync(request.StylistId, request.ScheduledAt, endsAt, cancellationToken);
        if (busyRanges.Any(b => request.ScheduledAt < b.EndsAt && b.StartsAt < endsAt))
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Slot is already booked."));

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

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new InitiateBookingResponse(
            booking.Id, booking.ScheduledAt, booking.ScheduledEndsAt, booking.TotalAmountMinor, booking.Currency));
    }
}
