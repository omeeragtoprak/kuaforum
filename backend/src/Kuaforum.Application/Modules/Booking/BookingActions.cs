using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record CancelBookingCommand(Guid ActorUserId, Guid BookingId, BookingCancellationReason Reason) : ICommand<int>;

public sealed class CancelBookingCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<CancelBookingCommand, Result<int>>
{
    public async Task<Result<int>> Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure<int>(Error.NotFound("Booking"));

        var allowed = await IsAuthorized(ctx, booking, request.ActorUserId, cancellationToken);
        if (!allowed) return Result.Failure<int>(Error.Forbidden("Cannot cancel this booking."));

        var cancelResult = booking.Cancel(request.Reason);
        if (cancelResult.IsFailure) return Result.Failure<int>(cancelResult.Error);

        await audit.RecordAsync(
            "booking.cancelled",
            nameof(BookingAggregate),
            booking.Id.ToString(),
            actorUserId: request.ActorUserId,
            payload: new { request.Reason, booking.RefundPercentage },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(booking.RefundPercentage ?? 0);
    }

    private static async Task<bool> IsAuthorized(DbContext ctx, BookingAggregate booking, Guid actorId, CancellationToken ct)
    {
        if (booking.CustomerId == actorId) return true;
        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, ct);
        return shop is not null && shop.OwnerId == actorId;
    }
}

public sealed record StartBookingCommand(Guid ActorUserId, Guid BookingId) : ICommand;

public sealed class StartBookingCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<StartBookingCommand, Result>
{
    public async Task<Result> Handle(StartBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure(Error.NotFound("Booking"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.ActorUserId)
            return Result.Failure(Error.Forbidden("Only barber owner can start booking."));

        var startResult = booking.Start();
        if (startResult.IsFailure) return startResult;

        await audit.RecordAsync("booking.started", nameof(BookingAggregate), booking.Id.ToString(),
            actorUserId: request.ActorUserId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record CompleteBookingCommand(Guid ActorUserId, Guid BookingId) : ICommand;

public sealed class CompleteBookingCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<CompleteBookingCommand, Result>
{
    public async Task<Result> Handle(CompleteBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure(Error.NotFound("Booking"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.ActorUserId)
            return Result.Failure(Error.Forbidden("Only barber owner can complete booking."));

        var result = booking.Complete();
        if (result.IsFailure) return result;

        await audit.RecordAsync("booking.completed", nameof(BookingAggregate), booking.Id.ToString(),
            actorUserId: request.ActorUserId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record MarkNoShowCommand(Guid ActorUserId, Guid BookingId) : ICommand;

public sealed class MarkNoShowCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<MarkNoShowCommand, Result>
{
    public async Task<Result> Handle(MarkNoShowCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure(Error.NotFound("Booking"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.ActorUserId)
            return Result.Failure(Error.Forbidden("Only barber owner can mark no-show."));

        var result = booking.MarkNoShow();
        if (result.IsFailure) return result;

        await audit.RecordAsync("booking.no_show", nameof(BookingAggregate), booking.Id.ToString(),
            actorUserId: request.ActorUserId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
