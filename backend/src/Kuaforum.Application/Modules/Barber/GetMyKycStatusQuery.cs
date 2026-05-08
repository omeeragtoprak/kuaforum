using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record MyKycStatusDto(
    Guid? SubmissionId,
    KycStatus? Status,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    int DocumentCount);

public sealed record GetMyKycStatusQuery(Guid OwnerUserId) : IQuery<MyKycStatusDto>;

/// Owner KYC ekranı için: son submission durumu. Reddedildiyse owner sebebi
/// görür ve yeniden başvurabilir. Hiç başvuru yoksa null fields döner.
public sealed class GetMyKycStatusQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyKycStatusQuery, Result<MyKycStatusDto>>
{
    public async Task<Result<MyKycStatusDto>> Handle(GetMyKycStatusQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<MyKycStatusDto>(Error.NotFound("BarberShop"));

        var latest = await ctx.Set<KycSubmission>().AsNoTracking()
            .Where(k => k.BarberShopId == shop.Id)
            .OrderByDescending(k => k.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return Result.Success(new MyKycStatusDto(null, null, null, null, null, 0));
        }

        var docCount = await ctx.Set<KycDocument>().AsNoTracking()
            .CountAsync(d => d.SubmissionId == latest.Id, ct);

        return Result.Success(new MyKycStatusDto(
            SubmissionId: latest.Id,
            Status: latest.Status,
            RejectionReason: latest.RejectionReason,
            SubmittedAt: latest.SubmittedAt,
            ReviewedAt: latest.ReviewedAt,
            DocumentCount: docCount));
    }
}
