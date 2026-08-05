using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Billing;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

/// <summary>RFC-012: owner borç ekranı + admin tahsilat kuyruğu endpoint'leri.</summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment env)
    {
        var ownerGroup = app.MapGroup("/v1/owner/billing").WithTags("Billing").RequireAuthorization("BarberOwner");

        ownerGroup.MapGet("/statements", async (IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetOwnerCommissionStatementsQuery(ctx.UserId.Value), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetOwnerCommissionStatements");

        ownerGroup.MapGet("/statements/{id:guid}", async (Guid id, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetOwnerCommissionStatementDetailQuery(ctx.UserId.Value, id), ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "not_found" ? 404 : r.Error.Code == "forbidden" ? 403 : 400);
        }).WithName("GetOwnerCommissionStatementDetail");

        ownerGroup.MapPost("/statements/{id:guid}/dispute", async (
            Guid id, [FromBody] DisputeRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new DisputeCommissionStatementCommand(ctx.UserId.Value, id, body.Note), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("DisputeCommissionStatement");

        ownerGroup.MapPost("/statements/{id:guid}/pay/initiate", async (
            Guid id, IMediator m, IRequestContext ctx, HttpContext http, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var idempKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var r = await m.Send(new InitiateCommissionPaymentCommand(ctx.UserId.Value, id, idempKey), ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "conflict" ? 409 : 400);
        }).WithName("InitiateCommissionPayment");

        // Mock-only completion endpoint for dev — PaymentEndpoints.MockCompleteAsync presedenti.
        // Prod'da map edilmez (anonim erişimli, tahmin edilebilir token).
        if (env.IsDevelopment())
        {
            ownerGroup.MapGet("/commission-payments/mock/complete", async (
                [FromQuery] string token, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new CompleteCommissionPaymentCommand(token, PaymentProviderName.Mock), ct);
                return r.IsSuccess
                    ? Results.Ok(new { commissionPaymentId = r.Value, message = "Mock commission payment captured." })
                    : Results.Problem(r.Error.Message, statusCode: 400);
            }).WithName("MockCompleteCommissionPayment").AllowAnonymous();
        }

        var adminGroup = app.MapGroup("/v1/admin/billing").WithTags("Billing").RequireAuthorization("Admin");

        adminGroup.MapGet("/settings", async (IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetPlatformSettingsQuery(), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetPlatformSettings");

        adminGroup.MapPut("/settings", async (
            [FromBody] UpdatePlatformSettingsRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdatePlatformSettingsCommand(
                ctx.UserId.Value, body.CommissionRateGlobal, body.CommissionPaymentIban,
                body.CommissionPaymentBankName, body.CommissionPaymentAccountHolderName), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("UpdatePlatformSettings");

        adminGroup.MapPut("/barber-shops/{id:guid}/commission-rate", async (
            Guid id, [FromBody] SetCommissionRateOverrideRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SetBarberShopCommissionRateOverrideCommand(ctx.UserId.Value, id, body.RateOverride), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SetBarberShopCommissionRateOverride");

        adminGroup.MapGet("/statements", async (
            [FromQuery] CommissionStatementStatus? status, [FromQuery] bool? disputedOnly,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminCommissionStatementsQueueQuery(status, disputedOnly, page ?? 1, pageSize ?? 25), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetAdminCommissionStatementsQueue");

        adminGroup.MapPost("/statements/{id:guid}/mark-paid", async (
            Guid id, [FromBody] AdminReasonRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new MarkCommissionStatementPaidCommand(ctx.UserId.Value, id, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("MarkCommissionStatementPaid");

        adminGroup.MapPost("/statements/{id:guid}/adjust", async (
            Guid id, [FromBody] AdjustAmountRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AdjustCommissionStatementAmountCommand(ctx.UserId.Value, id, body.NewAmountDueMinor, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AdjustCommissionStatementAmount");

        adminGroup.MapPost("/statements/{id:guid}/extend-due", async (
            Guid id, [FromBody] ExtendDueDateRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ExtendCommissionStatementDueDateCommand(ctx.UserId.Value, id, body.NewDueDate, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("ExtendCommissionStatementDueDate");

        return app;
    }

    public sealed record DisputeRequest(string Note);
    public sealed record UpdatePlatformSettingsRequest(
        decimal CommissionRateGlobal,
        string? CommissionPaymentIban,
        string? CommissionPaymentBankName,
        string? CommissionPaymentAccountHolderName);
    public sealed record SetCommissionRateOverrideRequest(decimal? RateOverride);
    public sealed record AdminReasonRequest(string Reason);
    public sealed record AdjustAmountRequest(long NewAmountDueMinor, string Reason);
    public sealed record ExtendDueDateRequest(DateOnly NewDueDate, string Reason);
}
