using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users;

public sealed record DeleteMyAccountCommand(Guid UserId) : ICommand;

public sealed class DeleteMyAccountCommandValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class DeleteMyAccountCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<DeleteMyAccountCommand, Result>
{
    public async Task<Result> Handle(DeleteMyAccountCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var soft = user.SoftDelete();
        if (soft.IsFailure) return soft;

        // Revoke all active refresh tokens
        var tokens = await ctx.Set<RefreshToken>()
            .Where(t => t.UserId == request.UserId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in tokens)
            t.Revoke(RefreshTokenRevokedReason.AccountDeleted);

        // Revoke active consents
        var consents = await ctx.Set<Consent>()
            .Where(c => c.UserId == request.UserId && c.RevokedAt == null && c.Granted)
            .ToListAsync(cancellationToken);
        foreach (var c in consents)
            c.Revoke();

        await audit.RecordAsync(
            "user.delete.requested",
            nameof(User),
            user.Id.ToString(),
            actorUserId: user.Id,
            payload: new { user.DeletedAt },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
