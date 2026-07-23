using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

/// <summary>
/// Admin KYC belge listesi + belge içeriği. Onaydan önce admin'in gerçek belgeyi
/// görmesini sağlar (bkz. GAP: "admin kör onay" — eskiden yalnız DocumentCount vardı).
/// Belgeler hassas (kimlik, vergi levhası) olduğundan her okuma audit log'a yazılır
/// ve içerik yalnızca bu auth'lu proxy endpoint'i üzerinden akıtılır — asla public
/// static-file URL üzerinden değil.
/// </summary>
public sealed record KycDocumentDto(
    Guid Id,
    DocumentType DocumentType,
    string FileName,
    string? ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record GetKycDocumentsQuery(Guid AdminUserId, Guid SubmissionId) : IQuery<IReadOnlyList<KycDocumentDto>>;

public sealed class GetKycDocumentsQueryHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<GetKycDocumentsQuery, Result<IReadOnlyList<KycDocumentDto>>>
{
    public async Task<Result<IReadOnlyList<KycDocumentDto>>> Handle(GetKycDocumentsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var submissionExists = await ctx.Set<KycSubmission>().AsNoTracking()
            .AnyAsync(k => k.Id == request.SubmissionId, ct);
        if (!submissionExists) return Result.Failure<IReadOnlyList<KycDocumentDto>>(Error.NotFound("KycSubmission"));

        var docs = await ctx.Set<KycDocument>().AsNoTracking()
            .Where(d => d.SubmissionId == request.SubmissionId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new KycDocumentDto(d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAt))
            .ToListAsync(ct);

        await audit.RecordAsync(
            "admin.kyc.documents_viewed",
            nameof(KycSubmission),
            request.SubmissionId.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { documentCount = docs.Count },
            ct: ct);
        await db.SaveChangesAsync(ct);

        return Result.Success<IReadOnlyList<KycDocumentDto>>(docs);
    }
}

/// Streaming metadata for a single KYC document. The Api layer resolves this to
/// an actual byte stream via IObjectStorage — kept out of Application to avoid
/// coupling MediatR handlers to raw I/O.
public sealed record KycDocumentContentDto(
    Guid Id,
    string StorageKey,
    string FileName,
    string ContentType);

public sealed record GetKycDocumentContentQuery(Guid AdminUserId, Guid DocumentId) : IQuery<KycDocumentContentDto>;

public sealed class GetKycDocumentContentQueryHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<GetKycDocumentContentQuery, Result<KycDocumentContentDto>>
{
    public async Task<Result<KycDocumentContentDto>> Handle(GetKycDocumentContentQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var doc = await ctx.Set<KycDocument>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct);
        if (doc is null) return Result.Failure<KycDocumentContentDto>(Error.NotFound("KycDocument"));

        await audit.RecordAsync(
            "admin.kyc.document_content_viewed",
            nameof(KycDocument),
            doc.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { doc.SubmissionId, doc.DocumentType },
            ct: ct);
        await db.SaveChangesAsync(ct);

        return Result.Success(new KycDocumentContentDto(
            doc.Id, doc.StorageKey, doc.FileName, doc.ContentType ?? "application/octet-stream"));
    }
}
