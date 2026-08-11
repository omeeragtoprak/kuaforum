using Kuaforum.Api.Common;
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
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminDashboard");

        group.MapGet("/kyc/queue", async (
            [FromQuery] KycStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetKycQueueQuery(status, page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminKycQueue");

        group.MapGet("/kyc/{submissionId:guid}/documents", async (
            Guid submissionId, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetKycDocumentsQuery(ctx.UserId.Value, submissionId), ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : r.ToProblem();
        }).WithName("AdminKycDocuments");

        group.MapGet("/kyc/documents/{documentId:guid}/content", async (
            Guid documentId, IMediator m, IObjectStorage storage, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetKycDocumentContentQuery(ctx.UserId.Value, documentId), ct);
            if (r.IsFailure)
                return r.ToProblem();

            try
            {
                var stream = await storage.OpenReadAsync(r.Value.StorageKey, ct);
                return Results.Stream(stream, r.Value.ContentType, fileDownloadName: r.Value.FileName);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("AdminKycDocumentContent");

        group.MapPost("/kyc/{submissionId:guid}/review", async (
            Guid submissionId, [FromBody] ReviewKycRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var cmd = new ReviewKycCommand(ctx.UserId.Value, submissionId, body.Approve, body.RejectionReason, body.SubMerchantKey);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("AdminReviewKyc");

        group.MapGet("/users", async (
            [FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] UserStatus? status,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetUsersQuery(search, role, status, page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminListUsers");

        group.MapPost("/users/{userId:guid}/suspend", async (
            Guid userId, [FromBody] SuspendUserRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SuspendUserCommand(ctx.UserId.Value, userId, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("AdminSuspendUser");

        group.MapPost("/users/{userId:guid}/unsuspend", async (
            Guid userId, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UnsuspendUserCommand(ctx.UserId.Value, userId), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
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
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminListBookings");

        group.MapGet("/barber-shops", async (
            [FromQuery] string? search, [FromQuery] BarberShopStatus? status,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminBarberShopsQuery(search, status, page ?? 1, pageSize ?? 25), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminListBarberShops");

        group.MapPost("/barber-shops/{id:guid}/suspend", async (
            Guid id, [FromBody] SuspendShopRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SuspendBarberShopCommand(ctx.UserId.Value, id, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("AdminSuspendBarberShop");

        group.MapPost("/barber-shops/{id:guid}/reactivate", async (
            Guid id, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ReactivateBarberShopCommand(ctx.UserId.Value, id), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("AdminReactivateBarberShop");

        group.MapPost("/bookings/{id:guid}/cancel", async (
            Guid id, [FromBody] AdminCancelBookingRequest body,
            IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AdminCancelBookingCommand(ctx.UserId.Value, id, body.Reason), ct);
            return r.IsSuccess
                ? Results.Ok(new { refundPercentage = r.Value })
                : r.ToProblem();
        }).WithName("AdminCancelBooking");

        // RFC-015 §2 D3 madde 3: müşteri şikayet kuyruğu — KYC kuyruğuyla aynı sayfalama kalıbı.
        group.MapGet("/booking-complaints", async (
            [FromQuery] BookingComplaintStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminBookingComplaintsQuery(status, page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminBookingComplaintQueue");

        group.MapGet("/audit-logs", async (
            [FromQuery] Guid? actorUserId,
            [FromQuery] string? actionType,
            [FromQuery] string? resourceType,
            [FromQuery] string? resourceId,
            [FromQuery] DateTimeOffset? fromUtc,
            [FromQuery] DateTimeOffset? toUtc,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAuditLogsQuery(
                actorUserId, actionType, resourceType, resourceId,
                fromUtc, toUtc, page ?? 1, pageSize ?? 50), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("AdminAuditLogs");

        return app;
    }

    public sealed record AdminCancelBookingRequest(string Reason);
    public sealed record SuspendShopRequest(string Reason);

    public sealed record ReviewKycRequest(bool Approve, string? RejectionReason, string? SubMerchantKey);
    public sealed record SuspendUserRequest(string Reason);
}
