using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Review.Events;

namespace Kuaforum.Domain.Modules.Review;

public sealed class Review : AggregateRoot
{
    private Review() { }

    public Guid CustomerId { get; private set; }
    public Guid BarberShopId { get; private set; }
    public Guid StylistId { get; private set; }
    public Guid BookingId { get; private set; }

    public int Rating { get; private set; }
    public string? Body { get; private set; }
    public ReviewModerationStatus ModerationStatus { get; private set; }
    public ReviewRejectionReason? RejectionReason { get; private set; }
    public DateTimeOffset? ModeratedAt { get; private set; }
    public Guid? ModeratedByUserId { get; private set; }

    public BarberReply? Reply { get; private set; }

    public static Result<Review> Submit(
        Guid customerId,
        Guid barberShopId,
        Guid stylistId,
        Guid bookingId,
        int rating,
        string? body)
    {
        if (rating is < 1 or > 5)
            return Result.Failure<Review>(Error.Validation("Rating must be between 1 and 5."));
        if (body is not null && body.Length > 1000)
            return Result.Failure<Review>(Error.Validation("Review body max 1000 chars."));

        var trimmedBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();

        var review = new Review
        {
            CustomerId = customerId,
            BarberShopId = barberShopId,
            StylistId = stylistId,
            BookingId = bookingId,
            Rating = rating,
            Body = trimmedBody,
            ModerationStatus = AutoModerate(trimmedBody),
        };

        if (review.ModerationStatus == ReviewModerationStatus.Approved)
        {
            review.ModeratedAt = DateTimeOffset.UtcNow;
        }

        review.RaiseDomainEvent(new ReviewSubmittedEvent(
            review.Id, customerId, barberShopId, stylistId, bookingId, rating));

        if (review.ModerationStatus == ReviewModerationStatus.Approved)
        {
            review.RaiseDomainEvent(new ReviewApprovedEvent(
                review.Id, customerId, barberShopId, stylistId, rating));
        }

        return review;
    }

    public Result Approve(Guid moderatorUserId)
    {
        if (ModerationStatus is ReviewModerationStatus.Approved or ReviewModerationStatus.Rejected)
            return Result.Failure(Error.Validation($"Cannot approve in status {ModerationStatus}."));

        ModerationStatus = ReviewModerationStatus.Approved;
        ModeratedAt = DateTimeOffset.UtcNow;
        ModeratedByUserId = moderatorUserId;
        Touch();
        RaiseDomainEvent(new ReviewApprovedEvent(Id, CustomerId, BarberShopId, StylistId, Rating));
        return Result.Success();
    }

    public Result Reject(Guid moderatorUserId, ReviewRejectionReason reason)
    {
        if (ModerationStatus == ReviewModerationStatus.Rejected) return Result.Success();

        ModerationStatus = ReviewModerationStatus.Rejected;
        RejectionReason = reason;
        ModeratedAt = DateTimeOffset.UtcNow;
        ModeratedByUserId = moderatorUserId;
        Touch();
        RaiseDomainEvent(new ReviewRejectedEvent(Id, reason));
        return Result.Success();
    }

    public Result Hide(Guid moderatorUserId)
    {
        ModerationStatus = ReviewModerationStatus.Hidden;
        ModeratedByUserId = moderatorUserId;
        ModeratedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result AddReply(Guid barberOwnerUserId, string replyText)
    {
        if (Reply is not null)
            return Result.Failure(Error.Validation("This review already has a reply."));
        if (string.IsNullOrWhiteSpace(replyText))
            return Result.Failure(Error.Validation("Reply text required."));
        if (replyText.Length > 500)
            return Result.Failure(Error.Validation("Reply max 500 chars."));
        if (ModerationStatus != ReviewModerationStatus.Approved)
            return Result.Failure(Error.Validation("Can only reply to approved reviews."));

        Reply = BarberReply.Create(Id, barberOwnerUserId, replyText.Trim());
        Touch();
        RaiseDomainEvent(new ReviewReplyAddedEvent(Id, barberOwnerUserId));
        return Result.Success();
    }

    private static ReviewModerationStatus AutoModerate(string? body)
    {
        if (string.IsNullOrEmpty(body)) return ReviewModerationStatus.Approved;
        if (ContainsProfanity(body)) return ReviewModerationStatus.Pending;
        return ReviewModerationStatus.Approved;
    }

    /// <summary>
    /// Basic Turkish profanity heuristic. Real production: integrate a proper
    /// content moderation service (Azure Content Safety, Perspective API, custom ML).
    /// </summary>
    private static bool ContainsProfanity(string body)
    {
        var lower = body.ToLowerInvariant();
        // intentionally minimal stopword list — heavy lifting belongs in a real service.
        var stopwords = new[] { "küfür", "salak", "aptal", "lan", "rezalet", "iğrenç" };
        return stopwords.Any(w => lower.Contains(w));
    }
}

public sealed class BarberReply : Entity
{
    private BarberReply() { }

    public Guid ReviewId { get; private set; }
    public Guid BarberOwnerUserId { get; private set; }
    public string Body { get; private set; } = null!;

    public static BarberReply Create(Guid reviewId, Guid ownerUserId, string body)
    {
        return new BarberReply
        {
            ReviewId = reviewId,
            BarberOwnerUserId = ownerUserId,
            Body = body,
        };
    }
}
