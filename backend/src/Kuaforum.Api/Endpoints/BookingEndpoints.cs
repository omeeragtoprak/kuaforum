using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/bookings").WithTags("Bookings").RequireAuthorization();

        group.MapPost("", InitiateAsync).WithName("InitiateBooking");
        group.MapGet("", GetMyBookingsAsync).WithName("GetMyBookings");
        group.MapGet("/{id:guid}", GetByIdAsync).WithName("GetBooking");
        group.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelBooking");
        group.MapPost("/{id:guid}/confirm", OwnerConfirmAsync).WithName("OwnerConfirmBooking");
        group.MapPost("/{id:guid}/start", StartAsync).WithName("StartBooking");
        group.MapPost("/{id:guid}/complete", CompleteAsync).WithName("CompleteBooking");
        group.MapPost("/{id:guid}/no-show", NoShowAsync).WithName("MarkNoShow");
        group.MapPost("/{id:guid}/reschedule", RescheduleAsync).WithName("RescheduleBooking");

        return app;
    }

    private static async Task<IResult> RescheduleAsync(
        Guid id,
        [FromBody] RescheduleBookingRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new RescheduleBookingCommand(ctx.UserId.Value, id, body.NewScheduledAt), token);
        return r.IsSuccess
            ? Results.Ok(r.Value)
            : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "conflict" ? 409 : 400);
    }

    private static async Task<IResult> InitiateAsync(
        [FromBody] InitiateBookingRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new InitiateBookingCommand(
            ctx.UserId.Value, body.StylistId, body.ScheduledAt, body.ServiceCatalogIds, body.IsWalkIn, body.CustomerNote), token);
        return r.IsSuccess
            ? Results.Created($"/v1/bookings/{r.Value.BookingId}", r.Value)
            : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "conflict" ? 409 : 400);
    }

    private static async Task<IResult> GetMyBookingsAsync(
        [FromQuery] BookingStatus? status,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new GetMyBookingsQuery(ctx.UserId.Value, status), token);
        return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
    }

    private static async Task<IResult> GetByIdAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new GetBookingByIdQuery(ctx.UserId.Value, id), token);
        return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new CancelBookingCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess
            ? Results.Ok(new { refundPercentage = r.Value })
            : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> OwnerConfirmAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new OwnerConfirmBookingCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> StartAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new StartBookingCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> CompleteAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new CompleteBookingCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> NoShowAsync(Guid id, IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new MarkNoShowCommand(ctx.UserId.Value, id), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    public sealed record InitiateBookingRequest(
        Guid StylistId,
        DateTimeOffset ScheduledAt,
        IReadOnlyList<Guid> ServiceCatalogIds,
        bool IsWalkIn,
        string? CustomerNote);

    public sealed record RescheduleBookingRequest(DateTimeOffset NewScheduledAt);
}
