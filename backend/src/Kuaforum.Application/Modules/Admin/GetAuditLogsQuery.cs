using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record AuditLogDto(
    long Id,
    Guid? ActorUserId,
    string? ActorDisplayName,
    AuditActorType ActorType,
    string ActionType,
    string ResourceType,
    string ResourceId,
    string? PayloadJson,
    string? Ip,
    string? RequestId,
    DateTimeOffset CreatedAt);

public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAuditLogsQuery(
    Guid? ActorUserId,
    string? ActionType,
    string? ResourceType,
    string? ResourceId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize) : IQuery<AuditLogPageDto>;

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.ActionType!).MaximumLength(64).When(x => !string.IsNullOrEmpty(x.ActionType));
        RuleFor(x => x.ResourceType!).MaximumLength(64).When(x => !string.IsNullOrEmpty(x.ResourceType));
        RuleFor(x => x.ResourceId!).MaximumLength(64).When(x => !string.IsNullOrEmpty(x.ResourceId));
    }
}

public sealed class GetAuditLogsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAuditLogsQuery, Result<AuditLogPageDto>>
{
    public async Task<Result<AuditLogPageDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var q = ctx.Set<AuditLog>().AsNoTracking().AsQueryable();

        if (request.ActorUserId is not null) q = q.Where(a => a.ActorUserId == request.ActorUserId);
        if (!string.IsNullOrEmpty(request.ActionType)) q = q.Where(a => a.ActionType == request.ActionType);
        if (!string.IsNullOrEmpty(request.ResourceType)) q = q.Where(a => a.ResourceType == request.ResourceType);
        if (!string.IsNullOrEmpty(request.ResourceId)) q = q.Where(a => a.ResourceId == request.ResourceId);
        if (request.FromUtc is not null) q = q.Where(a => a.CreatedAt >= request.FromUtc);
        if (request.ToUtc is not null) q = q.Where(a => a.CreatedAt < request.ToUtc);

        var total = await q.CountAsync(ct);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 200);

        // JOIN actor display name (left) — system actors null ActorUserId
        var users = ctx.Set<User>().AsNoTracking();
        var items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new AuditLogDto(
                a.Id,
                a.ActorUserId,
                a.ActorUserId == null
                    ? null
                    : users.Where(u => u.Id == a.ActorUserId.Value)
                        .Select(u => u.DisplayName ?? u.Email ?? u.PhoneE164)
                        .FirstOrDefault(),
                a.ActorType,
                a.ActionType,
                a.ResourceType,
                a.ResourceId,
                a.PayloadJson,
                a.Ip,
                a.RequestId,
                a.CreatedAt))
            .ToListAsync(ct);

        return Result.Success(new AuditLogPageDto(items, page, size, total));
    }
}
