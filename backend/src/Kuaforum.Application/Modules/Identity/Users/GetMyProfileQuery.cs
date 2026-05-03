using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Users;

public sealed record GetMyProfileQuery(Guid UserId) : IQuery<MyProfileDto>;

public sealed record MyProfileDto(
    Guid Id,
    string? Phone,
    string? Email,
    string? DisplayName,
    string? AvatarUrl,
    UserRole Role,
    UserStatus Status,
    string Locale,
    string TimeZone,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeletedAt);

public sealed class GetMyProfileQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyProfileQuery, Result<MyProfileDto>>
{
    public async Task<Result<MyProfileDto>> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<MyProfileDto>(Error.NotFound("User"));

        return Result.Success(new MyProfileDto(
            user.Id,
            user.PhoneE164,
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            user.Role,
            user.Status,
            user.Locale,
            user.TimeZone,
            user.CreatedAt,
            user.LastLoginAt,
            user.DeletedAt));
    }
}
