using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users;

public sealed record RestoreMyAccountCommand(Guid UserId) : ICommand;

public sealed class RestoreMyAccountCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<RestoreMyAccountCommand, Result>
{
    public async Task<Result> Handle(RestoreMyAccountCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var restore = user.RestoreFromSoftDelete();
        if (restore.IsFailure) return restore;

        await audit.RecordAsync(
            "user.delete.restored",
            nameof(User),
            user.Id.ToString(),
            actorUserId: user.Id,
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
