using Kuaforum.Domain.Modules.Review;
using Kuaforum.Domain.Modules.Review.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Review;

public class ReviewTests
{
    private static Kuaforum.Domain.Modules.Review.Review Submit(string? body = "Çok memnun kaldım.", int rating = 5)
    {
        return Kuaforum.Domain.Modules.Review.Review.Submit(
            customerId: Guid.NewGuid(),
            barberShopId: Guid.NewGuid(),
            stylistId: Guid.NewGuid(),
            bookingId: Guid.NewGuid(),
            rating: rating,
            body: body).Value;
    }

    [Fact]
    public void Submit_WithCleanBody_AutoApproves()
    {
        var review = Submit("Harika hizmet.");
        Assert.Equal(ReviewModerationStatus.Approved, review.ModerationStatus);
        Assert.Contains(review.DomainEvents, e => e is ReviewSubmittedEvent);
        Assert.Contains(review.DomainEvents, e => e is ReviewApprovedEvent);
    }

    [Fact]
    public void Submit_WithProfanity_GoesToPending()
    {
        var review = Submit("Salon rezalet bir hizmet verdi.");
        Assert.Equal(ReviewModerationStatus.Pending, review.ModerationStatus);
        Assert.Contains(review.DomainEvents, e => e is ReviewSubmittedEvent);
        Assert.DoesNotContain(review.DomainEvents, e => e is ReviewApprovedEvent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Submit_WithRatingOutsideRange_Fails(int rating)
    {
        var result = Kuaforum.Domain.Modules.Review.Review.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), rating, "OK");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Submit_WithBodyTooLong_Fails()
    {
        var longBody = new string('a', 1001);
        var result = Kuaforum.Domain.Modules.Review.Review.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, longBody);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Approve_OnPending_TransitionsToApproved()
    {
        var review = Submit("rezalet bir deneyim");
        var moderatorId = Guid.NewGuid();
        var r = review.Approve(moderatorId);
        Assert.True(r.IsSuccess);
        Assert.Equal(ReviewModerationStatus.Approved, review.ModerationStatus);
        Assert.Equal(moderatorId, review.ModeratedByUserId);
        Assert.NotNull(review.ModeratedAt);
    }

    [Fact]
    public void Approve_OnAlreadyApproved_Fails()
    {
        var review = Submit("güzel"); // auto-Approved
        var r = review.Approve(Guid.NewGuid());
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Reject_OnPending_TransitionsToRejected()
    {
        var review = Submit("salak personel");
        var r = review.Reject(Guid.NewGuid(), ReviewRejectionReason.PersonalAttack);
        Assert.True(r.IsSuccess);
        Assert.Equal(ReviewModerationStatus.Rejected, review.ModerationStatus);
        Assert.Equal(ReviewRejectionReason.PersonalAttack, review.RejectionReason);
    }

    [Fact]
    public void Reject_OnAlreadyRejected_IsIdempotent()
    {
        var review = Submit("aptal yaklaşım");
        review.Reject(Guid.NewGuid(), ReviewRejectionReason.Spam);
        var second = review.Reject(Guid.NewGuid(), ReviewRejectionReason.OffTopic);
        Assert.True(second.IsSuccess); // domain explicit: re-reject no-op
        Assert.Equal(ReviewRejectionReason.Spam, review.RejectionReason); // ilk reason korunmuş olmalı
    }

    [Fact]
    public void Hide_OnApproved_TransitionsToHidden()
    {
        var review = Submit("memnunum");
        var r = review.Hide(Guid.NewGuid());
        Assert.True(r.IsSuccess);
        Assert.Equal(ReviewModerationStatus.Hidden, review.ModerationStatus);
        Assert.Contains(review.DomainEvents, e => e is ReviewHiddenEvent);
    }

    [Fact]
    public void Hide_OnAlreadyHidden_Fails()
    {
        var review = Submit("güzel");
        review.Hide(Guid.NewGuid());
        var second = review.Hide(Guid.NewGuid());
        Assert.True(second.IsFailure);
    }

    [Fact]
    public void AddReply_OnApproved_Succeeds()
    {
        var review = Submit("güzel iş");
        var ownerId = Guid.NewGuid();
        var r = review.AddReply(ownerId, "Teşekkür ederiz!");
        Assert.True(r.IsSuccess);
        Assert.NotNull(review.Reply);
        Assert.Equal(ownerId, review.Reply!.BarberOwnerUserId);
        Assert.Equal("Teşekkür ederiz!", review.Reply.Body);
        Assert.Contains(review.DomainEvents, e => e is ReviewReplyAddedEvent);
    }

    [Fact]
    public void AddReply_OnPending_Fails()
    {
        var review = Submit("iğrenç");
        var r = review.AddReply(Guid.NewGuid(), "Cevap yazayım.");
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void AddReply_Twice_Fails()
    {
        var review = Submit("güzel");
        review.AddReply(Guid.NewGuid(), "İlk cevap");
        var second = review.AddReply(Guid.NewGuid(), "İkinci cevap");
        Assert.True(second.IsFailure);
    }

    [Fact]
    public void AddReply_EmptyBody_Fails()
    {
        var review = Submit("güzel");
        var r = review.AddReply(Guid.NewGuid(), "   ");
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void AddReply_TooLongBody_Fails()
    {
        var review = Submit("güzel");
        var r = review.AddReply(Guid.NewGuid(), new string('x', 501));
        Assert.True(r.IsFailure);
    }
}
