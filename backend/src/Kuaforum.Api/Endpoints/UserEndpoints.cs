using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Identity.Auth;
using Kuaforum.Application.Modules.Identity.Consents;
using Kuaforum.Application.Modules.Identity.Users;
using Kuaforum.Application.Modules.Identity.Users.Sessions;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/users/me").WithTags("Users").RequireAuthorization();

        group.MapGet("", GetMyProfile).WithName("GetMyProfile");
        group.MapPatch("", UpdateMyProfile).WithName("UpdateMyProfile");
        group.MapDelete("", DeleteMyAccount).WithName("DeleteMyAccount");
        group.MapPost("/restore", RestoreMyAccount).WithName("RestoreMyAccount");

        group.MapGet("/sessions", GetMySessions).WithName("GetMySessions");
        group.MapDelete("/sessions/{id:guid}", RevokeSession).WithName("RevokeSession");

        group.MapGet("/consents", GetMyConsents).WithName("GetMyConsents");
        group.MapPatch("/consents", UpdateConsent).WithName("UpdateConsent");

        group.MapPost("/phone-change/initiate", InitiatePhoneChange).WithName("InitiatePhoneChange");
        group.MapPost("/phone-change/confirm", ConfirmPhoneChange).WithName("ConfirmPhoneChange");

        return app;
    }

    private static async Task<IResult> InitiatePhoneChange(
        [FromBody] PhoneChangeInitiateRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new InitiatePhoneChangeCommand(ctx.UserId.Value, body.NewPhone), token);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> ConfirmPhoneChange(
        [FromBody] PhoneChangeConfirmRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new ConfirmPhoneChangeCommand(ctx.UserId.Value, body.OtpId, body.Code), token);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> GetMyProfile(IMediator m, IRequestContext ctx, CancellationToken ct)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new GetMyProfileQuery(ctx.UserId.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(detail: result.Error.Message, statusCode: 404, title: result.Error.Code);
    }

    private static async Task<IResult> UpdateMyProfile(
        [FromBody] UpdateMyProfileRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new UpdateMyProfileCommand(ctx.UserId.Value, body.DisplayName, body.AvatarUrl), token);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> DeleteMyAccount(IMediator m, IRequestContext ctx, CancellationToken ct)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new DeleteMyAccountCommand(ctx.UserId.Value), ct);
        return result.IsSuccess
            ? Results.Ok(new { message = "Account scheduled for deletion. You have 30 days to restore." })
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> RestoreMyAccount(IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new RestoreMyAccountCommand(ctx.UserId.Value), token);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    private static async Task<IResult> GetMySessions(IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new GetMySessionsQuery(ctx.UserId.Value), token);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(result.Error.Message);
    }

    private static async Task<IResult> RevokeSession(
        Guid id,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new RevokeSessionCommand(ctx.UserId.Value, id), token);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 404, title: result.Error.Code);
    }

    private static async Task<IResult> GetMyConsents(IMediator m, IRequestContext ctx, CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new GetMyConsentsQuery(ctx.UserId.Value), token);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(result.Error.Message);
    }

    private static async Task<IResult> UpdateConsent(
        [FromBody] UpdateConsentRequest body,
        IMediator m,
        IRequestContext ctx,
        CancellationToken token)
    {
        if (ctx.UserId is null) return Results.Unauthorized();
        var result = await m.Send(new UpdateConsentCommand(ctx.UserId.Value, body.Purpose, body.Grant, body.Version), token);
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
    }

    public sealed record UpdateMyProfileRequest(string? DisplayName, string? AvatarUrl);
    public sealed record UpdateConsentRequest(ConsentPurpose Purpose, bool Grant, string Version);
    public sealed record PhoneChangeInitiateRequest(string NewPhone);
    public sealed record PhoneChangeConfirmRequest(Guid OtpId, string Code);
}
