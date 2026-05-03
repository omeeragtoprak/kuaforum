using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber;

public sealed class KycSubmission : Entity
{
    private KycSubmission() { }

    public Guid BarberShopId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public KycStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    public static KycSubmission Create(Guid barberShopId, Guid submittedByUserId)
    {
        return new KycSubmission
        {
            BarberShopId = barberShopId,
            SubmittedByUserId = submittedByUserId,
            Status = KycStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
    }

    public Result MarkUnderReview(Guid reviewerId)
    {
        if (Status != KycStatus.Pending)
            return Result.Failure(Error.Validation("Only pending submissions can move to review."));
        Status = KycStatus.UnderReview;
        ReviewedByUserId = reviewerId;
        Touch();
        return Result.Success();
    }

    public Result Approve(Guid reviewerId)
    {
        Status = KycStatus.Approved;
        ReviewedByUserId = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Reject(Guid reviewerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Rejection requires reason."));
        Status = KycStatus.Rejected;
        ReviewedByUserId = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        RejectionReason = reason;
        Touch();
        return Result.Success();
    }
}

public sealed class KycDocument : Entity
{
    private KycDocument() { }

    public Guid SubmissionId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!; // R2 key
    public long SizeBytes { get; private set; }
    public string? ContentType { get; private set; }

    public static Result<KycDocument> Create(
        Guid submissionId,
        DocumentType type,
        string fileName,
        string storageKey,
        long sizeBytes,
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return Result.Failure<KycDocument>(Error.Validation("FileName required."));
        if (string.IsNullOrWhiteSpace(storageKey)) return Result.Failure<KycDocument>(Error.Validation("StorageKey required."));
        if (sizeBytes <= 0) return Result.Failure<KycDocument>(Error.Validation("Size must be > 0."));
        if (sizeBytes > 10 * 1024 * 1024) return Result.Failure<KycDocument>(Error.Validation("File exceeds 10MB."));

        return new KycDocument
        {
            SubmissionId = submissionId,
            DocumentType = type,
            FileName = fileName,
            StorageKey = storageKey,
            SizeBytes = sizeBytes,
            ContentType = contentType,
        };
    }
}
