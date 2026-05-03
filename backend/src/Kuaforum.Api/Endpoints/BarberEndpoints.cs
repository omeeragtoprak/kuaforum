using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Barber;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class BarberEndpoints
{
    public static IEndpointRouteBuilder MapBarberEndpoints(this IEndpointRouteBuilder app)
    {
        var owned = app.MapGroup("/v1/barber-shops").WithTags("BarberShops");

        // Public read
        owned.MapGet("/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetBarberShopByIdQuery(id), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
        }).AllowAnonymous().WithName("GetBarberShop");

        owned.MapGet("/{id:guid}/stylists", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetStylistsByShopQuery(id), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).AllowAnonymous().WithName("GetStylistsByShop");

        owned.MapGet("/{id:guid}/services", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetServicesByShopQuery(id), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).AllowAnonymous().WithName("GetServicesByShop");

        // Owner-protected
        var ownerGroup = app.MapGroup("/v1/owner/barber-shop").WithTags("BarberShops").RequireAuthorization();

        ownerGroup.MapGet("", async (IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetMyBarberShopQuery(ctx.UserId.Value), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetMyBarberShop");

        ownerGroup.MapPost("", async ([FromBody] CreateBarberShopRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new CreateBarberShopCommand(ctx.UserId.Value, body.Name, body.GenderType, body.Description, body.Phone), token);
            return r.IsSuccess ? Results.Created($"/v1/barber-shops/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("CreateBarberShop");

        ownerGroup.MapPatch("/{id:guid}", async (Guid id, [FromBody] UpdateBarberShopRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdateBarberShopCommand(ctx.UserId.Value, id, body.Name, body.Description, body.GenderType, body.Phone), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("UpdateBarberShop");

        ownerGroup.MapPut("/{id:guid}/address", async (Guid id, [FromBody] SetAddressRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SetBarberShopAddressCommand(
                ctx.UserId.Value, id, body.CityId, body.DistrictId, body.NeighborhoodId,
                body.Line1, body.Line2, body.PostalCode, body.Latitude, body.Longitude), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SetBarberShopAddress");

        ownerGroup.MapPost("/{id:guid}/stylists", async (Guid id, [FromBody] AddStylistRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AddStylistCommand(ctx.UserId.Value, id, body.DisplayName, body.UserId), token);
            return r.IsSuccess ? Results.Created($"/v1/stylists/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AddStylist");

        ownerGroup.MapPost("/{id:guid}/services", async (Guid id, [FromBody] CreateServiceRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new CreateServiceCommand(ctx.UserId.Value, id, body.Name, body.Gender, body.DurationMinutes, body.PriceMinor, body.BufferMinutes, body.Description), token);
            return r.IsSuccess ? Results.Created($"/v1/services/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("CreateService");

        ownerGroup.MapPost("/{id:guid}/kyc/submit", async (Guid id, [FromBody] SubmitKycRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SubmitKycCommand(ctx.UserId.Value, id, body.Documents), token);
            return r.IsSuccess ? Results.Ok(new { submissionId = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SubmitKyc");

        // Stylist actions
        var stylistGroup = app.MapGroup("/v1/stylists").WithTags("Stylists").RequireAuthorization();

        stylistGroup.MapPatch("/{id:guid}", async (Guid id, [FromBody] UpdateStylistRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdateStylistCommand(ctx.UserId.Value, id, body.DisplayName, body.PhotoUrl, body.Bio, body.IsActive), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("UpdateStylist");

        stylistGroup.MapPost("/{id:guid}/walk-in", async (Guid id, [FromBody] ToggleWalkInRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new ToggleStylistWalkInCommand(ctx.UserId.Value, id, body.Available), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("ToggleWalkIn");

        stylistGroup.MapPut("/{id:guid}/working-hours", async (Guid id, [FromBody] WorkingHoursRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SetWorkingHoursCommand(ctx.UserId.Value, id, body.DayOfWeek, body.OpenTime, body.CloseTime, body.IsClosed), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SetWorkingHours");

        stylistGroup.MapPost("/{id:guid}/time-off", async (Guid id, [FromBody] TimeOffRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AddTimeOffCommand(ctx.UserId.Value, id, body.StartsAt, body.EndsAt, body.Reason), token);
            return r.IsSuccess ? Results.Created($"/v1/time-offs/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AddTimeOff");

        return app;
    }

    public sealed record CreateBarberShopRequest(string Name, SalonGenderType GenderType, string? Description, string? Phone);
    public sealed record UpdateBarberShopRequest(string? Name, string? Description, SalonGenderType? GenderType, string? Phone);
    public sealed record SetAddressRequest(Guid CityId, Guid DistrictId, Guid NeighborhoodId, string Line1, string? Line2, string? PostalCode, double Latitude, double Longitude);
    public sealed record AddStylistRequest(string DisplayName, Guid? UserId);
    public sealed record UpdateStylistRequest(string? DisplayName, string? PhotoUrl, string? Bio, bool? IsActive);
    public sealed record ToggleWalkInRequest(bool Available);
    public sealed record CreateServiceRequest(string Name, ServiceGender Gender, int DurationMinutes, long PriceMinor, int BufferMinutes, string? Description);
    public sealed record WorkingHoursRequest(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime, bool IsClosed);
    public sealed record TimeOffRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
    public sealed record SubmitKycRequest(IReadOnlyList<KycDocumentRequest> Documents);
}
