using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Auth;

public sealed record LogoutCommand(string RefreshToken) : ICommand;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class LogoutCommandHandler(
    IApplicationDbContext db,
    IJwtTokenService jwtTokenService) : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var hash = jwtTokenService.HashRefreshToken(request.RefreshToken);
        var token = await ctx.Set<RefreshToken>().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null) return Result.Success();

        token.Revoke(RefreshTokenRevokedReason.UserLogout);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record LogoutAllCommand(Guid UserId) : ICommand;

public sealed class LogoutAllCommandHandler(IApplicationDbContext db) : IRequestHandler<LogoutAllCommand, Result>
{
    public async Task<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var tokens = await ctx.Set<RefreshToken>()
            .Where(t => t.UserId == request.UserId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in tokens)
            t.Revoke(RefreshTokenRevokedReason.UserLogout);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
