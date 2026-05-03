using Kuaforum.Application.Modules.Availability;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/availability").WithTags("Availability").AllowAnonymous();

        group.MapPost("/compute", async (
            [FromBody] ComputeAvailabilityRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var result = await m.Send(new ComputeAvailabilityQuery(
                body.StylistId, body.FromDate, body.ToDate, body.ServiceCatalogIds), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(detail: result.Error.Message, statusCode: 400, title: result.Error.Code);
        }).WithName("ComputeAvailability");

        return app;
    }

    public sealed record ComputeAvailabilityRequest(
        Guid StylistId,
        DateOnly FromDate,
        DateOnly ToDate,
        IReadOnlyList<Guid> ServiceCatalogIds);
}
