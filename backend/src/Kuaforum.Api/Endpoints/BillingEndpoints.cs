using Kuaforum.Api.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Billing;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

/// <summary>
/// RFC-012 (RFC-014 §2 D1 ile abonelik faturasına genelleştirildi): owner borç ekranı + admin
/// tahsilat kuyruğu endpoint'leri. Route YOLLARI RFC-012'den bilerek DEĞİŞMEDİ (mobil/web-admin
/// bu yollara bağlı) — yalnız yanıt gövdelerindeki alanlar (basisAmountMinor/rateApplied →
/// planCode/planPriceMinor) değişti, bkz. GÖREV notu.
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment env)
    {
        var ownerGroup = app.MapGroup("/v1/owner/billing").WithTags("Billing").RequireAuthorization("BarberOwner");

        ownerGroup.MapGet("/statements", async (IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetOwnerSubscriptionInvoicesQuery(ctx.UserId.Value), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("GetOwnerSubscriptionInvoices");

        ownerGroup.MapGet("/statements/{id:guid}", async (Guid id, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetOwnerSubscriptionInvoiceDetailQuery(ctx.UserId.Value, id), ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : r.ToProblem();
        }).WithName("GetOwnerSubscriptionInvoiceDetail");

        ownerGroup.MapPost("/statements/{id:guid}/dispute", async (
            Guid id, [FromBody] DisputeRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new DisputeSubscriptionInvoiceCommand(ctx.UserId.Value, id, body.Note), ct);
            return r.IsSuccess
                ? Results.NoContent()
                : r.ToProblem();
        }).WithName("DisputeSubscriptionInvoice");

        ownerGroup.MapPost("/statements/{id:guid}/pay/initiate", async (
            Guid id, IMediator m, IRequestContext ctx, HttpContext http, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var idempKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var r = await m.Send(new InitiateSubscriptionInvoicePaymentCommand(ctx.UserId.Value, id, idempKey), ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : r.ToProblem();
        }).WithName("InitiateSubscriptionInvoicePayment");

        // Mock-only completion endpoint for dev — PaymentEndpoints.MockCompleteAsync presedenti.
        // Prod'da map edilmez (anonim erişimli, tahmin edilebilir token).
        if (env.IsDevelopment())
        {
            ownerGroup.MapGet("/subscription-invoice-payments/mock/complete", async (
                [FromQuery] string token, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new CompleteSubscriptionInvoicePaymentCommand(token, PaymentProviderName.Mock), ct);
                return r.IsSuccess
                    ? Results.Ok(new { subscriptionInvoicePaymentId = r.Value, message = "Mock subscription invoice payment captured." })
                    : r.ToProblem();
            }).WithName("MockCompleteSubscriptionInvoicePayment").AllowAnonymous();
        }

        var adminGroup = app.MapGroup("/v1/admin/billing").WithTags("Billing").RequireAuthorization("Admin");

        adminGroup.MapGet("/settings", async (IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetPlatformSettingsQuery(), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("GetPlatformSettings");

        adminGroup.MapPut("/settings", async (
            [FromBody] UpdatePlatformSettingsRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdatePlatformSettingsCommand(
                ctx.UserId.Value, body.CommissionPaymentIban,
                body.CommissionPaymentBankName, body.CommissionPaymentAccountHolderName,
                body.DepositPercentMin, body.DepositPercentMax), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("UpdatePlatformSettings");

        adminGroup.MapGet("/statements", async (
            [FromQuery] SubscriptionInvoiceStatus? status, [FromQuery] bool? disputedOnly,
            [FromQuery] DateOnly? periodStart,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetAdminSubscriptionInvoicesQueueQuery(status, disputedOnly, periodStart, page ?? 1, pageSize ?? 25), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.ToProblem();
        }).WithName("GetAdminSubscriptionInvoicesQueue");

        adminGroup.MapPost("/statements/{id:guid}/mark-paid", async (
            Guid id, [FromBody] AdminReasonRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new MarkSubscriptionInvoicePaidCommand(ctx.UserId.Value, id, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("MarkSubscriptionInvoicePaid");

        adminGroup.MapPost("/statements/{id:guid}/adjust", async (
            Guid id, [FromBody] AdjustAmountRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AdjustSubscriptionInvoiceAmountCommand(ctx.UserId.Value, id, body.NewAmountMinor, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("AdjustSubscriptionInvoiceAmount");

        adminGroup.MapPost("/statements/{id:guid}/extend-due", async (
            Guid id, [FromBody] ExtendDueDateRequest body, IMediator m, IRequestContext ctx, CancellationToken ct) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ExtendSubscriptionInvoiceDueDateCommand(ctx.UserId.Value, id, body.NewDueDate, body.Reason), ct);
            return r.IsSuccess ? Results.NoContent() : r.ToProblem();
        }).WithName("ExtendSubscriptionInvoiceDueDate");

        return app;
    }

    public sealed record DisputeRequest(string Note);
    public sealed record UpdatePlatformSettingsRequest(
        string? CommissionPaymentIban,
        string? CommissionPaymentBankName,
        string? CommissionPaymentAccountHolderName,
        decimal DepositPercentMin,
        decimal DepositPercentMax);
    public sealed record AdminReasonRequest(string Reason);
    public sealed record AdjustAmountRequest(long NewAmountMinor, string Reason);
    public sealed record ExtendDueDateRequest(DateOnly NewDueDate, string Reason);
}
