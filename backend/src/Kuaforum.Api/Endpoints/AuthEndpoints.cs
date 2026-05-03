using Kuaforum.Application.Modules.Identity.Auth;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/auth").WithTags("Auth");

        group.MapPost("/otp/send", SendOtpAsync)
            .WithName("SendOtp")
            .AllowAnonymous();

        group.MapPost("/otp/verify", VerifyOtpAsync)
            .WithName("VerifyOtp")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .RequireAuthorization();

        group.MapPost("/logout/all", LogoutAllAsync)
            .WithName("LogoutAll")
            .RequireAuthorization();

        group.MapPost("/oauth/{provider}", OAuthLoginAsync)
            .WithName("OAuthLogin")
            .AllowAnonymous();

        group.MapPost("/admin/login", AdminLoginAsync)
            .WithName("AdminLogin")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> AdminLoginAsync(
        [FromBody] AdminLoginRequest request,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var deviceFingerprint = request.DeviceFingerprint
            ?? $"web-admin-{httpContext.Connection.Id}";
        var result = await mediator.Send(
            new AdminLoginCommand(request.Email, request.Password, request.TotpCode,
                deviceFingerprint, request.DeviceName ?? "Web Admin", ip), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 401, title: result.Error.Code);
    }

    private static async Task<IResult> OAuthLoginAsync(
        string provider,
        [FromBody] OAuthLoginRequest request,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var result = await mediator.Send(
            new OAuthLoginCommand(provider, request.IdToken, request.DeviceFingerprint, request.DeviceName, ip),
            ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 401, title: result.Error.Code);
    }

    private static async Task<IResult> SendOtpAsync(
        [FromBody] SendOtpRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new SendOtpCommand(request.Phone, request.Purpose), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> VerifyOtpAsync(
        [FromBody] VerifyOtpRequest request,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var result = await mediator.Send(
            new VerifyOtpCommand(request.OtpId, request.Code, request.DeviceFingerprint, request.DeviceName, ip),
            ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var result = await mediator.Send(new RefreshTokenCommand(request.RefreshToken, request.DeviceFingerprint, ip), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 401, title: result.Error.Code);
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new LogoutCommand(request.RefreshToken), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken ct)
    {
        var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();
        var result = await mediator.Send(new LogoutAllCommand(userId), ct);
        return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error.Message);
    }

    public sealed record SendOtpRequest(string Phone, OtpPurpose Purpose);
    public sealed record VerifyOtpRequest(Guid OtpId, string Code, string DeviceFingerprint, string? DeviceName);
    public sealed record RefreshRequest(string RefreshToken, string DeviceFingerprint);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record OAuthLoginRequest(string IdToken, string DeviceFingerprint, string? DeviceName);
    public sealed record AdminLoginRequest(string Email, string Password, string TotpCode, string? DeviceFingerprint, string? DeviceName);
}
