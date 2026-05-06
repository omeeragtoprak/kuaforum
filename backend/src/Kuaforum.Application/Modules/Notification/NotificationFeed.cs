using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Notification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Notification;

public sealed record NotificationFeedItemDto(
    Guid Id,
    string TemplateKey,
    string Subject,
    string Body,
    bool IsRead,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    Guid? RelatedBookingId);

public sealed record NotificationFeedDto(
    int UnreadCount,
    IReadOnlyList<NotificationFeedItemDto> Items);

/// <summary>
/// Returns the user's in-app notification feed.
/// Push/SMS/Email entries are excluded — only InApp channel rows are surfaced.
/// </summary>
public sealed record GetMyNotificationsQuery(Guid UserId, int Take) : IQuery<NotificationFeedDto>;

public sealed class GetMyNotificationsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyNotificationsQuery, Result<NotificationFeedDto>>
{
    public async Task<Result<NotificationFeedDto>> Handle(GetMyNotificationsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var take = request.Take <= 0 ? 50 : Math.Min(request.Take, 200);

        var rows = await ctx.Set<NotificationLog>().AsNoTracking()
            .Where(n => n.UserId == request.UserId && n.Channel == NotificationChannel.InApp)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationFeedItemDto(
                n.Id, n.TemplateKey, n.Subject, n.Body, n.IsRead, n.ReadAt, n.CreatedAt, n.RelatedBookingId))
            .ToListAsync(ct);

        var unread = await ctx.Set<NotificationLog>().AsNoTracking()
            .CountAsync(n => n.UserId == request.UserId && n.Channel == NotificationChannel.InApp && !n.IsRead, ct);

        return Result.Success(new NotificationFeedDto(unread, rows));
    }
}

public sealed record GetUnreadCountQuery(Guid UserId) : IQuery<int>;

public sealed class GetUnreadCountQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetUnreadCountQuery, Result<int>>
{
    public async Task<Result<int>> Handle(GetUnreadCountQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var unread = await ctx.Set<NotificationLog>().AsNoTracking()
            .CountAsync(n => n.UserId == request.UserId && n.Channel == NotificationChannel.InApp && !n.IsRead, ct);
        return Result.Success(unread);
    }
}

public sealed record MarkNotificationReadCommand(Guid UserId, Guid NotificationId) : ICommand;

public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NotificationId).NotEmpty();
    }
}

public sealed class MarkNotificationReadCommandHandler(IApplicationDbContext db)
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var entry = await ctx.Set<NotificationLog>()
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, ct);

        if (entry is null) return Result.Failure(Error.NotFound("Notification"));
        if (entry.UserId != request.UserId)
            return Result.Failure(Error.Forbidden("Bu bildirim size ait değil."));

        entry.MarkRead();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record MarkAllNotificationsReadCommand(Guid UserId) : ICommand<int>;

public sealed class MarkAllNotificationsReadCommandValidator : AbstractValidator<MarkAllNotificationsReadCommand>
{
    public MarkAllNotificationsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class MarkAllNotificationsReadCommandHandler(IApplicationDbContext db)
    : IRequestHandler<MarkAllNotificationsReadCommand, Result<int>>
{
    public async Task<Result<int>> Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var unread = await ctx.Set<NotificationLog>()
            .Where(n => n.UserId == request.UserId
                     && n.Channel == NotificationChannel.InApp
                     && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
            n.MarkRead();

        await db.SaveChangesAsync(ct);
        return Result.Success(unread.Count);
    }
}
