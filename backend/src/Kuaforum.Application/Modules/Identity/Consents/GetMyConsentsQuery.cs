using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Consents;

public sealed record GetMyConsentsQuery(Guid UserId) : IQuery<IReadOnlyList<ConsentDto>>;

public sealed record ConsentDto(
    ConsentPurpose Purpose,
    bool Granted,
    string Version,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt);

public sealed class GetMyConsentsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyConsentsQuery, Result<IReadOnlyList<ConsentDto>>>
{
    public async Task<Result<IReadOnlyList<ConsentDto>>> Handle(GetMyConsentsQuery request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var consents = await ctx.Set<Consent>().AsNoTracking()
            .Where(c => c.UserId == request.UserId)
            .GroupBy(c => c.Purpose)
            .Select(g => g.OrderByDescending(c => c.GrantedAt).First())
            .Select(c => new ConsentDto(c.Purpose, c.Granted, c.Version, c.GrantedAt, c.RevokedAt))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ConsentDto>>(consents);
    }
}
