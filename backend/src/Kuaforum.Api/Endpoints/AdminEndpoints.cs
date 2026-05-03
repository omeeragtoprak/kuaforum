using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Admin;
using Kuaforum.Application.Modules.Barber;
using Kuaforum.Domain.Modules.Barber;
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

        return app;
    }

    public sealed record ReviewKycRequest(bool Approve, string? RejectionReason, string? SubMerchantKey);
}
