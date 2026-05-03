using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users;

public sealed record UpdateMyProfileCommand(
    Guid UserId,
    string? DisplayName,
    string? AvatarUrl) : ICommand;

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.DisplayName).MaximumLength(80).When(x => x.DisplayName is not null);
        RuleFor(x => x.AvatarUrl).MaximumLength(500).When(x => x.AvatarUrl is not null);
    }
}

public sealed class UpdateMyProfileCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<UpdateMyProfileCommand, Result>
{
    public async Task<Result> Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var update = user.UpdateProfile(request.DisplayName, request.AvatarUrl);
        if (update.IsFailure) return update;

        await audit.RecordAsync(
            "user.profile.update",
            nameof(User),
            user.Id.ToString(),
            actorUserId: user.Id,
            payload: new { request.DisplayName, request.AvatarUrl },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
