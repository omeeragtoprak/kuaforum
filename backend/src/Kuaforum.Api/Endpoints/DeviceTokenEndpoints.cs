using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Identity.DeviceTokens;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class DeviceTokenEndpoints
{
    public static IEndpointRouteBuilder MapDeviceTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/users/me/device-tokens")
            .WithTags("DeviceTokens")
            .RequireAuthorization();

        group.MapPost("", RegisterAsync).WithName("RegisterDeviceToken");
        group.MapDelete("", UnregisterAsync).WithName("UnregisterDeviceToken");

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterDeviceTokenRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new RegisterDeviceTokenCommand(
            ctx.UserId.Value, body.FcmToken, body.Platform, body.AppVersion, body.DeviceModel), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    private static async Task<IResult> UnregisterAsync(
        [FromQuery] string fcmToken,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var r = await m.Send(new UnregisterDeviceTokenCommand(ctx.UserId.Value, fcmToken), token);
        return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
    }

    public sealed record RegisterDeviceTokenRequest(
        string FcmToken,
        DevicePlatform Platform,
        string? AppVersion,
        string? DeviceModel);
}
