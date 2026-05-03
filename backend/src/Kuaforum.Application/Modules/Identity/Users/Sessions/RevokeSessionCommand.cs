using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users.Sessions;

public sealed record RevokeSessionCommand(Guid UserId, Guid SessionId) : ICommand;

public sealed class RevokeSessionCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<RevokeSessionCommand, Result>
{
    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var token = await ctx.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.Id == request.SessionId && t.UserId == request.UserId, cancellationToken);
        if (token is null) return Result.Failure(Error.NotFound("Session"));
        if (token.IsRevoked) return Result.Success();

        token.Revoke(RefreshTokenRevokedReason.UserLogout);
        await audit.RecordAsync(
            "user.session.revoked",
            nameof(RefreshToken),
            token.Id.ToString(),
            actorUserId: request.UserId,
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
