using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Booking;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record AdminCancelBookingCommand(
    Guid AdminUserId,
    Guid BookingId,
    string Reason) : ICommand<int>;

public sealed class AdminCancelBookingCommandValidator : AbstractValidator<AdminCancelBookingCommand>
{
    public AdminCancelBookingCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class AdminCancelBookingCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<AdminCancelBookingCommand, Result<int>>
{
    public async Task<Result<int>> Handle(AdminCancelBookingCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().FirstOrDefaultAsync(b => b.Id == request.BookingId, ct);
        if (booking is null) return Result.Failure<int>(Error.NotFound("Booking"));

        // Admin override: full refund + audit + recorded reason
        var result = booking.Cancel(BookingCancellationReason.AdminOverride);
        if (result.IsFailure) return Result.Failure<int>(result.Error);

        await audit.RecordAsync("admin.booking.cancelled", nameof(BookingAggregate), booking.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { reason = request.Reason },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success(booking.RefundPercentage ?? 100);
    }
}
