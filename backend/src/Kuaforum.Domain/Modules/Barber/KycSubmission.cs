using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber.Events;

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
        var entry = new KycSubmission
        {
            BarberShopId = barberShopId,
            SubmittedByUserId = submittedByUserId,
            Status = KycStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        entry.RaiseDomainEvent(new KycSubmittedEvent(entry.Id, barberShopId, submittedByUserId));
        return entry;
    }

    public Result MarkUnderReview(Guid reviewerId)
    {
        if (Status != KycStatus.Pending)
            return Result.Failure(Error.Validation("Yalnızca beklemede olan başvurular incelemeye alınabilir."));
        Status = KycStatus.UnderReview;
        ReviewedByUserId = reviewerId;
        Touch();

        RaiseDomainEvent(new KycUnderReviewEvent(Id, reviewerId));
        return Result.Success();
    }

    public Result Approve(Guid reviewerId)
    {
        if (Status is KycStatus.Approved or KycStatus.Rejected)
            return Result.Failure(Error.Validation($"{Status} durumundaki başvuru onaylanamaz."));
        Status = KycStatus.Approved;
        ReviewedByUserId = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new KycApprovedEvent(Id, BarberShopId, SubmittedByUserId, reviewerId));
        return Result.Success();
    }

    public Result Reject(Guid reviewerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Reddetme için gerekçe belirtilmeli."));
        if (Status is KycStatus.Approved or KycStatus.Rejected)
            return Result.Failure(Error.Validation($"{Status} durumundaki başvuru reddedilemez."));
        Status = KycStatus.Rejected;
        ReviewedByUserId = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        RejectionReason = reason;
        Touch();

        RaiseDomainEvent(new KycRejectedEvent(Id, BarberShopId, SubmittedByUserId, reviewerId, reason));
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
        if (string.IsNullOrWhiteSpace(fileName)) return Result.Failure<KycDocument>(Error.Validation("Dosya adı gerekli."));
        if (string.IsNullOrWhiteSpace(storageKey)) return Result.Failure<KycDocument>(Error.Validation("Depolama anahtarı gerekli."));
        if (sizeBytes <= 0) return Result.Failure<KycDocument>(Error.Validation("Dosya boyutu sıfırdan büyük olmalı."));
        if (sizeBytes > 10 * 1024 * 1024) return Result.Failure<KycDocument>(Error.Validation("Dosya boyutu 10MB sınırını aşıyor."));

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
