using Kuaforum.Api.Common;
using Kuaforum.Application.Modules.Geography;
using MediatR;

namespace Kuaforum.Api.Endpoints;

public static class GeographyEndpoints
{
    public static IEndpointRouteBuilder MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/geography").WithTags("Geography").AllowAnonymous();

        group.MapGet("/cities", async (IMediator m, CancellationToken ct) =>
        {
            var result = await m.Send(new GetCitiesQuery(), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).WithName("GetCities");

        group.MapGet("/cities/{cityId:guid}/districts", async (Guid cityId, IMediator m, CancellationToken ct) =>
        {
            var result = await m.Send(new GetDistrictsByCityQuery(cityId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).WithName("GetDistricts");

        group.MapGet("/districts/{districtId:guid}/neighborhoods", async (Guid districtId, IMediator m, CancellationToken ct) =>
        {
            var result = await m.Send(new GetNeighborhoodsByDistrictQuery(districtId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).WithName("GetNeighborhoods");

        return app;
    }
}
