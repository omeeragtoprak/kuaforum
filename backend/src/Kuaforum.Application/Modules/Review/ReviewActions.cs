using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using Kuaforum.Domain.Modules.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Review;

public sealed record ApproveReviewCommand(Guid ModeratorUserId, Guid ReviewId) : ICommand;

public sealed class ApproveReviewCommandValidator : AbstractValidator<ApproveReviewCommand>
{
    public ApproveReviewCommandValidator()
    {
        RuleFor(x => x.ModeratorUserId).NotEmpty();
        RuleFor(x => x.ReviewId).NotEmpty();
    }
}

public sealed class ApproveReviewCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<ApproveReviewCommand, Result>
{
    public async Task<Result> Handle(ApproveReviewCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var review = await ctx.Set<ReviewAggregate>().FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);
        if (review is null) return Result.Failure(Error.NotFound("Review"));

        var result = review.Approve(request.ModeratorUserId);
        if (result.IsFailure) return result;

        await audit.RecordAsync("review.approved", nameof(ReviewAggregate), review.Id.ToString(),
            actorUserId: request.ModeratorUserId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record RejectReviewCommand(Guid ModeratorUserId, Guid ReviewId, ReviewRejectionReason Reason) : ICommand;

public sealed class RejectReviewCommandValidator : AbstractValidator<RejectReviewCommand>
{
    public RejectReviewCommandValidator()
    {
        RuleFor(x => x.ModeratorUserId).NotEmpty();
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Reason).IsInEnum();
    }
}

public sealed class RejectReviewCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<RejectReviewCommand, Result>
{
    public async Task<Result> Handle(RejectReviewCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var review = await ctx.Set<ReviewAggregate>().FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);
        if (review is null) return Result.Failure(Error.NotFound("Review"));

        var result = review.Reject(request.ModeratorUserId, request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync("review.rejected", nameof(ReviewAggregate), review.Id.ToString(),
            actorUserId: request.ModeratorUserId, payload: new { Reason = request.Reason.ToString() },
            ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ReplyToReviewCommand(Guid BarberOwnerUserId, Guid ReviewId, string Body) : ICommand;

public sealed class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReplyToReviewCommandHandler(IApplicationDbContext db, IAuditLogger audit) : IRequestHandler<ReplyToReviewCommand, Result>
{
    public async Task<Result> Handle(ReplyToReviewCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var review = await ctx.Set<ReviewAggregate>()
            .Include(r => r.Reply)
            .FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);
        if (review is null) return Result.Failure(Error.NotFound("Review"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == review.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.BarberOwnerUserId)
            return Result.Failure(Error.Forbidden("Yalnızca işletme sahibi yanıt verebilir."));

        var result = review.AddReply(request.BarberOwnerUserId, request.Body);
        if (result.IsFailure) return result;

        // EF Core navigation set ettiğimizde Reply'ı Modified olarak track ediyor
        // (Include null ile attach edildiği için). Yeni satır → manuel Added.
        ctx.Set<BarberReply>().Add(review.Reply!);

        await audit.RecordAsync("review.reply_added", nameof(ReviewAggregate), review.Id.ToString(),
            actorUserId: request.BarberOwnerUserId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
