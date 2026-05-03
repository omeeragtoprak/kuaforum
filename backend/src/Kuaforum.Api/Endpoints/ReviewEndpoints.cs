using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Review;
using Kuaforum.Domain.Modules.Review;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        // Public read on shop reviews
        app.MapGet("/v1/barber-shops/{shopId:guid}/reviews", async (
                Guid shopId, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new GetReviewsByShopQuery(shopId, page ?? 1, pageSize ?? 20), ct);
                return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
            })
            .WithTags("Reviews").AllowAnonymous().WithName("GetShopReviews");

        var customer = app.MapGroup("/v1/reviews").WithTags("Reviews").RequireAuthorization();

        customer.MapPost("", async ([FromBody] SubmitReviewRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SubmitReviewCommand(ctx.UserId.Value, body.BookingId, body.Rating, body.Body), token);
            return r.IsSuccess ? Results.Created($"/v1/reviews/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SubmitReview");

        customer.MapPost("/{id:guid}/reply", async (Guid id, [FromBody] ReplyRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ReplyToReviewCommand(ctx.UserId.Value, id, body.Body), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("ReplyToReview");

        // Admin moderation
        var admin = app.MapGroup("/v1/admin/reviews").WithTags("ReviewsAdmin").RequireAuthorization("Admin");
        admin.MapGet("/pending", async ([FromQuery] int? page, [FromQuery] int? pageSize, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetPendingReviewsQuery(page ?? 1, pageSize ?? 20), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetPendingReviews");

        admin.MapPost("/{id:guid}/approve", async (Guid id, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ApproveReviewCommand(ctx.UserId.Value, id), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("ApproveReview");

        admin.MapPost("/{id:guid}/reject", async (Guid id, [FromBody] RejectRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new RejectReviewCommand(ctx.UserId.Value, id, body.Reason), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("RejectReview");

        return app;
    }

    public sealed record SubmitReviewRequest(Guid BookingId, int Rating, string? Body);
    public sealed record ReplyRequest(string Body);
    public sealed record RejectRequest(ReviewRejectionReason Reason);
}
