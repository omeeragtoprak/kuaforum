using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record AdminUserDto(
    Guid Id,
    string? PhoneE164,
    string? Email,
    string? DisplayName,
    UserRole Role,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeletedAt);

public sealed record GetUsersQuery(
    string? SearchTerm,
    UserRole? RoleFilter,
    UserStatus? StatusFilter,
    int Page,
    int PageSize) : IQuery<UsersAdminPage>;

public sealed record UsersAdminPage(IReadOnlyList<AdminUserDto> Items, int TotalCount, int Page, int PageSize);

public sealed class GetUsersQueryHandler(IApplicationDbContext db) : IRequestHandler<GetUsersQuery, Result<UsersAdminPage>>
{
    public async Task<Result<UsersAdminPage>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<User>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLowerInvariant();
            query = query.Where(u =>
                (u.PhoneE164 != null && u.PhoneE164.Contains(term))
                || (u.EmailNormalized != null && u.EmailNormalized.Contains(term))
                || (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)));
        }

        if (request.RoleFilter.HasValue) query = query.Where(u => u.Role == request.RoleFilter.Value);
        if (request.StatusFilter.HasValue) query = query.Where(u => u.Status == request.StatusFilter.Value);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(u => new AdminUserDto(
                u.Id, u.PhoneE164, u.Email, u.DisplayName, u.Role, u.Status,
                u.CreatedAt, u.LastLoginAt, u.DeletedAt))
            .ToListAsync(ct);

        return Result.Success(new UsersAdminPage(items, total, page, size));
    }
}

public sealed record SuspendUserCommand(Guid AdminUserId, Guid TargetUserId, string Reason) : ICommand;

public sealed class SuspendUserCommandValidator : AbstractValidator<SuspendUserCommand>
{
    public SuspendUserCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class SuspendUserCommandHandler(IApplicationDbContext db, IAuditLogger audit, IJwtTokenService jwt)
    : IRequestHandler<SuspendUserCommand, Result>
{
    public async Task<Result> Handle(SuspendUserCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.TargetUserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var result = user.Suspend();
        if (result.IsFailure) return result;

        // Revoke all active refresh tokens immediately
        var tokens = await ctx.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in tokens) t.Revoke(RefreshTokenRevokedReason.AdminRevoke);

        await audit.RecordAsync("admin.user.suspended", nameof(User), user.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { request.Reason },
            ct: ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record UnsuspendUserCommand(Guid AdminUserId, Guid TargetUserId) : ICommand;

public sealed class UnsuspendUserCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<UnsuspendUserCommand, Result>
{
    public async Task<Result> Handle(UnsuspendUserCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var user = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.TargetUserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var result = user.Unsuspend();
        if (result.IsFailure) return result;

        await audit.RecordAsync("admin.user.unsuspended", nameof(User), user.Id.ToString(),
            actorUserId: request.AdminUserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
