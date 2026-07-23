using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record SubmitKycCommand(
    Guid OwnerId,
    Guid BarberShopId,
    IReadOnlyList<KycDocumentRequest> Documents) : ICommand<Guid>;

public sealed record KycDocumentRequest(
    DocumentType DocumentType,
    string FileName,
    string StorageKey,
    long SizeBytes,
    string? ContentType);

public sealed class SubmitKycCommandValidator : AbstractValidator<SubmitKycCommand>
{
    public SubmitKycCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.Documents).NotEmpty().Must(d => d.Count >= 2)
            .WithMessage("En az 2 belge gerekli (vergi levhası + işyeri açma ruhsatı).");
    }
}

public sealed class SubmitKycCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<SubmitKycCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(SubmitKycCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure<Guid>(Error.NotFound("BarberShop"));
        if (shop.OwnerId != request.OwnerId) return Result.Failure<Guid>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var hasPending = await ctx.Set<KycSubmission>()
            .AnyAsync(k => k.BarberShopId == request.BarberShopId && (k.Status == KycStatus.Pending || k.Status == KycStatus.UnderReview), cancellationToken);
        if (hasPending) return Result.Failure<Guid>(Error.Conflict("Halihazırda incelemede olan bir KYC başvurunuz var."));

        var submission = KycSubmission.Create(request.BarberShopId, request.OwnerId);
        ctx.Set<KycSubmission>().Add(submission);

        foreach (var doc in request.Documents)
        {
            var docResult = KycDocument.Create(submission.Id, doc.DocumentType, doc.FileName, doc.StorageKey, doc.SizeBytes, doc.ContentType);
            if (docResult.IsFailure) return Result.Failure<Guid>(docResult.Error);
            ctx.Set<KycDocument>().Add(docResult.Value);
        }

        var markPending = shop.MarkPendingKyc();
        if (markPending.IsFailure) return Result.Failure<Guid>(markPending.Error);

        await audit.RecordAsync(
            "barber.kyc.submitted",
            nameof(KycSubmission),
            submission.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new { documentCount = request.Documents.Count, types = request.Documents.Select(d => d.DocumentType).ToArray() },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(submission.Id);
    }
}

public sealed record ReviewKycCommand(
    Guid AdminUserId,
    Guid SubmissionId,
    bool Approve,
    string? RejectionReason,
    string? SubMerchantKey) : ICommand;

public sealed class ReviewKycCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<ReviewKycCommand, Result>
{
    public async Task<Result> Handle(ReviewKycCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var submission = await ctx.Set<KycSubmission>().FirstOrDefaultAsync(k => k.Id == request.SubmissionId, cancellationToken);
        if (submission is null) return Result.Failure(Error.NotFound("KycSubmission"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == submission.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        if (request.Approve)
        {
            if (string.IsNullOrEmpty(request.SubMerchantKey))
                return Result.Failure(Error.Validation("Onaylama için alt üye işyeri anahtarı gerekli (PSP kayıt)."));

            var approve = submission.Approve(request.AdminUserId);
            if (approve.IsFailure) return approve;

            var shopApprove = shop.ApproveKyc(request.SubMerchantKey);
            if (shopApprove.IsFailure) return shopApprove;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                return Result.Failure(Error.Validation("Reddetme gerekçesi belirtilmeli."));
            var reject = submission.Reject(request.AdminUserId, request.RejectionReason);
            if (reject.IsFailure) return reject;

            // Reject shop'u PendingKyc'de kilitli bırakırsa owner asla yeniden başvuramaz
            // (MarkPendingKyc yalnızca Draft'tan geçişe izin veriyor). Draft'a döndür.
            var revert = shop.RevertToDraft();
            if (revert.IsFailure) return revert;
        }

        await audit.RecordAsync(
            request.Approve ? "barber.kyc.approved" : "barber.kyc.rejected",
            nameof(KycSubmission),
            submission.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { request.RejectionReason },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
