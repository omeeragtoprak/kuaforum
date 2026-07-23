using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Payment;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment env)
    {
        var group = app.MapGroup("/v1/payments").WithTags("Payments");

        group.MapPost("/initiate", InitiateAsync)
            .WithName("InitiatePayment")
            .RequireAuthorization();

        // Refund tutarı client'tan gelmez (booking.RefundPercentage'dan türetilir) ve yalnızca
        // Admin tetikleyebilir — bkz. RefundPaymentCommand XML doc.
        group.MapPost("/refund", RefundAsync)
            .WithName("RefundPayment")
            .RequireAuthorization("Admin");

        // Mock-only completion endpoint for dev. Production: PayTR/Iyzico webhooks.
        // Dev dışında map edilmez — anonim erişimli ve token'ı tahmin edilebilir olduğu için
        // prod'da mevcut olmamalı (bkz. AuthEndpoints.MapAuthEndpoints dev-login kalıbı).
        if (env.IsDevelopment())
        {
            group.MapGet("/mock/complete", MockCompleteAsync)
                .WithName("MockCompletePayment")
                .AllowAnonymous();
        }

        return app;
    }

    private static async Task<IResult> InitiateAsync(
        [FromBody] InitiateRequest body,
        IMediator m,
        IRequestContext ctx,
        HttpContext http,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var idempKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        var r = await m.Send(new InitiatePaymentCommand(ctx.UserId.Value, body.BookingId, idempKey), token);
        return r.IsSuccess
            ? Results.Ok(r.Value)
            : Results.Problem(r.Error.Message, statusCode: r.Error.Code == "conflict" ? 409 : 400);
    }

    private static async Task<IResult> RefundAsync(
        [FromBody] RefundRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new RefundPaymentCommand(ctx.UserId.Value, body.PaymentId), token);
        return r.IsSuccess
            ? Results.Ok(new { refundId = r.Value })
            : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> MockCompleteAsync(
        [FromQuery] string token,
        IMediator m,
        CancellationToken ct)
    {
        var r = await m.Send(new CompletePaymentCommand(token, PaymentProviderName.Mock), ct);
        return r.IsSuccess
            ? Results.Ok(new { paymentId = r.Value, message = "Mock payment captured. Booking confirmed." })
            : Results.Problem(r.Error.Message, statusCode: 400);
    }

    public sealed record InitiateRequest(Guid BookingId);
    public sealed record RefundRequest(Guid PaymentId);
}
