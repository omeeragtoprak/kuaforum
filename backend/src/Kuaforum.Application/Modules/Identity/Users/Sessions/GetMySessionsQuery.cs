using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users.Sessions;

public sealed record GetMySessionsQuery(Guid UserId) : IQuery<IReadOnlyList<SessionDto>>;

public sealed record SessionDto(
    Guid Id,
    string DeviceFingerprint,
    string? DeviceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp,
    bool IsCurrent);

public sealed class GetMySessionsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetMySessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var sessions = await ctx.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.UserId == request.UserId && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
            .Select(t => new SessionDto(
                t.Id,
                t.DeviceFingerprint,
                t.DeviceName,
                t.CreatedAt,
                t.ExpiresAt,
                t.LastUsedAt,
                t.LastUsedIp,
                false))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<SessionDto>>(sessions);
    }
}
