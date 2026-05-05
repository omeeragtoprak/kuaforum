using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Notification;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("", GetMineAsync).WithName("GetMyNotifications");
        group.MapGet("/unread-count", GetUnreadCountAsync).WithName("GetUnreadNotificationCount");
        group.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        group.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        [FromQuery] int? take,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new GetMyNotificationsQuery(ctx.UserId.Value, take ?? 50), token);
        return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
    }

    private static async Task<IResult> GetUnreadCountAsync(IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new GetUnreadCountQuery(ctx.UserId.Value), token);
        return r.IsSuccess ? Results.Ok(new { unreadCount = r.Value }) : Results.Problem(r.Error.Message);
    }

    private static async Task<IResult> MarkReadAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new MarkNotificationReadCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess
            ? Results.NoContent()
            : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "not_found" ? 404 : 400);
    }

    private static async Task<IResult> MarkAllReadAsync(IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new MarkAllNotificationsReadCommand(ctx.UserId.Value), token);
        return r.IsSuccess ? Results.Ok(new { markedRead = r.Value }) : Results.Problem(r.Error.Message);
    }
}
