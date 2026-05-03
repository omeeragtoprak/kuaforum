using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Review;

public sealed record SubmitReviewCommand(
    Guid CustomerId,
    Guid BookingId,
    int Rating,
    string? Body) : ICommand<Guid>;

public sealed class SubmitReviewCommandValidator : AbstractValidator<SubmitReviewCommand>
{
    public SubmitReviewCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body).MaximumLength(1000).When(x => x.Body is not null);
    }
}

public sealed class SubmitReviewCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<SubmitReviewCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(SubmitReviewCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure<Guid>(Error.NotFound("Booking"));
        if (booking.CustomerId != request.CustomerId)
            return Result.Failure<Guid>(Error.Forbidden("Not your booking."));
        if (booking.Status != BookingStatus.Completed)
            return Result.Failure<Guid>(Error.Validation("Can only review completed bookings."));

        var existing = await ctx.Set<ReviewAggregate>()
            .AnyAsync(r => r.BookingId == request.BookingId, cancellationToken);
        if (existing) return Result.Failure<Guid>(Error.Conflict("Review for this booking already exists."));

        var reviewResult = ReviewAggregate.Submit(
            request.CustomerId,
            booking.BarberShopId,
            booking.StylistId,
            booking.Id,
            request.Rating,
            request.Body);
        if (reviewResult.IsFailure) return Result.Failure<Guid>(reviewResult.Error);

        var review = reviewResult.Value;
        ctx.Set<ReviewAggregate>().Add(review);

        await audit.RecordAsync(
            "review.submitted",
            nameof(ReviewAggregate),
            review.Id.ToString(),
            actorUserId: request.CustomerId,
            payload: new { booking.BarberShopId, booking.StylistId, review.Rating, review.ModerationStatus },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(review.Id);
    }
}
