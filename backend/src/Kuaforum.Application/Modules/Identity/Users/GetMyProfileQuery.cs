using Kuaforum.Application.Common;
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
    DateTimeOffset? DeletedAt,
    bool RequiresTosConsent);

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

        // RequiresTosConsent: güncel versiyonla granted bir ToS satırı yoksa true (satır hiç
        // yoksa da null-safe true — AnyAsync boş sonuçta false döner, negation true olur).
        var hasCurrentTos = await ctx.Set<Consent>().AsNoTracking()
            .AnyAsync(c => c.UserId == request.UserId
                && c.Purpose == ConsentPurpose.TermsOfService
                && c.Granted
                && c.RevokedAt == null
                && c.Version == LegalDocuments.CurrentTosVersion, cancellationToken);

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
            user.DeletedAt,
            RequiresTosConsent: !hasCurrentTos));
    }
}
