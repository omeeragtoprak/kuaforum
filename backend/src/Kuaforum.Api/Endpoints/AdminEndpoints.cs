using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Admin;
using Kuaforum.Application.Modules.Barber;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin").WithTags("Admin").RequireAuthorization("Admin");

        group.MapGet("/dashboard", async (IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetDashboardStatsQuery(), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("AdminDashboard");

        group.MapGet("/kyc/queue", async (
            [FromQuery] KycStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetKycQueueQuery(status, page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("AdminKycQueue");

        group.MapPost("/kyc/{submissionId:guid}/review", async (
            Guid submissionId, [FromBody] ReviewKycRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var cmd = new ReviewKycCommand(ctx.UserId.Value, submissionId, body.Approve, body.RejectionReason, body.SubMerchantKey);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AdminReviewKyc");

        group.MapGet("/users", async (
            [FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] UserStatus? status,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetUsersQuery(search, role, status, page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("AdminListUsers");

        group.MapPost("/users/{userId:guid}/suspend", async (
            Guid userId, [FromBody] SuspendUserRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SuspendUserCommand(ctx.UserId.Value, userId, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AdminSuspendUser");

        group.MapPost("/users/{userId:guid}/unsuspend", async (
            Guid userId, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UnsuspendUserCommand(ctx.UserId.Value, userId), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AdminUnsuspendUser");

        group.MapGet("/bookings", async (
            [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
            [FromQuery] BookingStatus? status, [FromQuery] Guid? barberShopId,
            [FromQuery] string? search,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminBookingsQuery(
                from, to, status, barberShopId, search, page ?? 1, pageSize ?? 25), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("AdminListBookings");

        group.MapGet("/barber-shops", async (
            [FromQuery] string? search, [FromQuery] BarberShopStatus? status,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminBarberShopsQuery(search, status, page ?? 1, pageSize ?? 25), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("AdminListBarberShops");

        group.MapPost("/bookings/{id:guid}/cancel", async (
            Guid id, [FromBody] AdminCancelBookingRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AdminCancelBookingCommand(ctx.UserId.Value, id, body.Reason), ct);
            return r.IsSuccess
                ? Results.Ok(new { refundPercentage = r.Value })
                : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AdminCancelBooking");

        return app;
    }

    public sealed record AdminCancelBookingRequest(string Reason);

    public sealed record ReviewKycRequest(bool Approve, string? RejectionReason, string? SubMerchantKey);
    public sealed record SuspendUserRequest(string Reason);
}
