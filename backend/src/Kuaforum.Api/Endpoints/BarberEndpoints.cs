using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Barber;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Kuaforum.Api.Endpoints;

public static class BarberEndpoints
{
    public static IEndpointRouteBuilder MapBarberEndpoints(this IEndpointRouteBuilder app)
    {
        var owned = app.MapGroup("/v1/barber-shops").WithTags("BarberShops");

        // Public search + read
        owned.MapGet("/search", async (
            [FromQuery] Guid? cityId,
            [FromQuery] Guid? districtId,
            [FromQuery] SalonGenderType? genderType,
            [FromQuery] bool? walkInOnly,
            [FromQuery] double? minRating,
            [FromQuery] long? maxPriceMinor,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new SearchBarbersQuery(
                CityId: cityId,
                DistrictId: districtId,
                GenderType: genderType,
                OpenWalkInOnly: walkInOnly,
                MinRating: minRating,
                MaxPriceMinor: maxPriceMinor,
                Page: page is null or <= 0 ? 1 : page.Value,
                PageSize: pageSize is null or <= 0 ? 20 : Math.Min(pageSize.Value, 50)), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).AllowAnonymous().WithName("SearchBarbers");

        owned.MapGet("/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetBarberShopByIdQuery(id), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
        }).AllowAnonymous().WithName("GetBarberShop");

        // AllowAnonymous ama authenticated ise IRequestContext.UserId dolu gelir (auth
        // middleware her istekte çalışır, AllowAnonymous yalnızca 401'i atlatır) — bu,
        // owner'ın kendi Draft/Suspended shop'unu görebilmesi için gereken sinyal.
        owned.MapGet("/{id:guid}/stylists", async (Guid id, IMediator m, IRequestContext reqCtx, CancellationToken ct) =>
        {
            var r = await m.Send(new GetStylistsByShopQuery(id, reqCtx.UserId), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
        }).AllowAnonymous().WithName("GetStylistsByShop");

        owned.MapGet("/{id:guid}/services", async (Guid id, IMediator m, IRequestContext reqCtx, CancellationToken ct) =>
        {
            var r = await m.Send(new GetServicesByShopQuery(id, reqCtx.UserId), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
        }).AllowAnonymous().WithName("GetServicesByShop");

        // RFC-011 — web landing + kuaför public profil. `owned` grubundaki
        // /{id:guid} vb. rotalar zaten AllowAnonymous ama BarberShopDto'yu
        // (OwnerId + CommissionTier dahil) OLDUĞU GİBİ dönüyor — o rotalar mobile
        // app'in kendi iç kullanımı içindir. Web landing farklı bir origin'den
        // (kuaforum.com) ve farklı bir güven sınırından çağıracağından dar
        // PublicBarberShopDto + ayrı /v1/public/barber-shops kök path'i şart.
        var publicGroup = app.MapGroup("/v1/public/barber-shops").WithTags("PublicBarberShops").AllowAnonymous();

        publicGroup.MapGet("/{slug}", async (string slug, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new GetPublicBarberShopBySlugQuery(slug), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound();
        }).WithName("GetPublicBarberShopBySlug");

        publicGroup.MapGet("", async (
            [FromQuery] Guid? cityId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new GetPublicBarberShopsQuery(
                CityId: cityId,
                Page: page is null or <= 0 ? 1 : page.Value,
                PageSize: pageSize is null or <= 0 ? 20 : Math.Min(pageSize.Value, 50)), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetPublicBarberShops");

        // Owner-protected (GAP P1-4): rol policy'si grup seviyesinde — Customer token'ı 403 alır.
        var ownerGroup = app.MapGroup("/v1/owner/barber-shop").WithTags("BarberShops").RequireAuthorization("BarberOwner");

        // Create-shop: rol geçiş tetiği — Customer da çağırabilir (RFC-007 self-serve onboarding);
        // rol guard'ı handler'da (Customer → promote, Admin → Forbidden). Grup policy'si
        // endpoint'ten geri alınamadığından istisna için AYRI grup şart.
        var onboardGroup = app.MapGroup("/v1/owner/barber-shop").WithTags("BarberShops").RequireAuthorization();

        onboardGroup.MapPost("", async ([FromBody] CreateBarberShopRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new CreateBarberShopCommand(ctx.UserId.Value, body.Name, body.GenderType, body.Description, body.Phone), token);
            return r.IsSuccess
                ? Results.Created($"/v1/barber-shops/{r.Value}", new { id = r.Value })
                : Results.Problem(r.Error.Message, statusCode: r.Error.Code switch { "conflict" => 409, "forbidden" => 403, _ => 400 });
        }).WithName("CreateBarberShop");

        ownerGroup.MapGet("", async (IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetMyBarberShopQuery(ctx.UserId.Value), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetMyBarberShop");

        ownerGroup.MapGet("/stats", async (IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetOwnerDashboardStatsQuery(ctx.UserId.Value), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetOwnerDashboardStats");

        ownerGroup.MapGet("/bookings", async (
            [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
            [FromQuery] BookingStatus? status,
            IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            // NOT (canlı sunucuda yakalanan gerçek bug, 2026-08-06): DateTimeOffset.UtcNow.Date
            // bir DateTime döner (DateTimeOffset DEĞİL) — implicit dönüşüm sunucunun YEREL saat
            // dilimini kullanır. Sunucu OS'i UTC değilse (bu makinede +03:00) Npgsql "timestamp
            // with time zone yalnız Offset=0 kabul eder" hatasıyla patlar — from/to query param'ı
            // VERİLMEDEN her owner bookings çağrısında (varsayılan yol). new DateTimeOffset(...,
            // TimeSpan.Zero) ile açıkça UTC'ye sabitleniyor.
            var fromUtc = from ?? new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
            var toUtc = to ?? fromUtc.AddDays(7);
            var r = await m.Send(new GetOwnerBookingsQuery(ctx.UserId.Value, fromUtc, toUtc, status), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
        }).WithName("GetOwnerBookings");

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

        ownerGroup.MapPut("/{id:guid}/services/{serviceId:guid}", async (Guid id, Guid serviceId, [FromBody] UpdateServiceRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdateServiceCommand(ctx.UserId.Value, id, serviceId, body.Name, body.Description, body.DurationMinutes, body.PriceMinor, body.BufferMinutes), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("UpdateService");

        ownerGroup.MapDelete("/{id:guid}/services/{serviceId:guid}", async (Guid id, Guid serviceId, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new DeleteServiceCommand(ctx.UserId.Value, id, serviceId), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("DeleteService");

        // RFC-013 C3: owner kendi shop'unun kapora + anti-spam ayarını görür/değiştirir.
        // ID parametresi yok — GetMyBarberShopQuery kalıbıyla aynı, ctx.UserId üzerinden bulunur.
        ownerGroup.MapGet("/booking-policy", async (IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetBookingPolicyQuery(ctx.UserId.Value), token);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.Problem(r.Error.Message, statusCode: r.Error.Code switch { "not_found" => 404, "forbidden" => 403, "server_misconfigured" => 500, _ => 400 });
        }).WithName("GetBookingPolicy");

        ownerGroup.MapPut("/booking-policy", async (
            [FromBody] UpdateBookingPolicyRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new UpdateBookingPolicyCommand(
                ctx.UserId.Value, body.DepositEnabled, body.DepositPercent,
                body.MaxPendingPerCustomer, body.MinDaysBetweenBookings, body.MaxBookingsPerMonth,
                body.AutoConfirmEnabled), token);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.Problem(r.Error.Message, statusCode: r.Error.Code switch { "not_found" => 404, "forbidden" => 403, "server_misconfigured" => 500, _ => 400 });
        }).WithName("UpdateBookingPolicy");

        ownerGroup.MapPost("/{id:guid}/kyc/submit", async (Guid id, [FromBody] SubmitKycRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SubmitKycCommand(ctx.UserId.Value, id, body.Documents), token);
            return r.IsSuccess ? Results.Ok(new { submissionId = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SubmitKyc");

        ownerGroup.MapGet("/kyc/status", async (IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new GetMyKycStatusQuery(ctx.UserId.Value), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetMyKycStatus");

        // Stylist actions (GAP P1-4): "Stylist" policy'si BarberOwner'ı da kapsar (AuthSetup).
        var stylistGroup = app.MapGroup("/v1/stylists").WithTags("Stylists").RequireAuthorization("Stylist");

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

        stylistGroup.MapGet("/{id:guid}/working-hours", async (Guid id, IMediator m, CancellationToken token) =>
        {
            var r = await m.Send(new GetWorkingHoursQuery(id), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetWorkingHours");

        stylistGroup.MapPut("/{id:guid}/working-hours", async (Guid id, [FromBody] WorkingHoursRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new SetWorkingHoursCommand(ctx.UserId.Value, id, body.DayOfWeek, body.OpenTime, body.CloseTime, body.IsClosed), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("SetWorkingHours");

        stylistGroup.MapGet("/{id:guid}/time-offs", async (Guid id, IMediator m, CancellationToken token) =>
        {
            var r = await m.Send(new GetTimeOffsQuery(id), token);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message, statusCode: 404);
        }).WithName("GetTimeOffs");

        stylistGroup.MapPost("/{id:guid}/time-off", async (Guid id, [FromBody] TimeOffRequest body, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new AddTimeOffCommand(ctx.UserId.Value, id, body.StartsAt, body.EndsAt, body.Reason), token);
            return r.IsSuccess ? Results.Created($"/v1/time-offs/{r.Value}", new { id = r.Value }) : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("AddTimeOff");

        stylistGroup.MapDelete("/{id:guid}/time-offs/{timeOffId:guid}", async (Guid id, Guid timeOffId, IMediator m, IRequestContext ctx, CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            var r = await m.Send(new DeleteTimeOffCommand(ctx.UserId.Value, id, timeOffId), token);
            return r.IsSuccess ? Results.NoContent() : Results.Problem(r.Error.Message, statusCode: 400);
        }).WithName("DeleteTimeOff");

        return app;
    }

    public sealed record CreateBarberShopRequest(string Name, SalonGenderType GenderType, string? Description, string? Phone);
    public sealed record UpdateBarberShopRequest(string? Name, string? Description, SalonGenderType? GenderType, string? Phone);
    public sealed record SetAddressRequest(Guid CityId, Guid DistrictId, Guid? NeighborhoodId, string Line1, string? Line2, string? PostalCode, double Latitude, double Longitude);
    public sealed record AddStylistRequest(string DisplayName, Guid? UserId);
    public sealed record UpdateStylistRequest(string? DisplayName, string? PhotoUrl, string? Bio, bool? IsActive);
    public sealed record ToggleWalkInRequest(bool Available);
    public sealed record CreateServiceRequest(string Name, ServiceGender Gender, int DurationMinutes, long PriceMinor, int BufferMinutes, string? Description);
    public sealed record UpdateServiceRequest(string? Name, string? Description, int? DurationMinutes, long? PriceMinor, int? BufferMinutes);
    public sealed record WorkingHoursRequest(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime, bool IsClosed);
    public sealed record TimeOffRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
    public sealed record SubmitKycRequest(IReadOnlyList<KycDocumentRequest> Documents);
    public sealed record UpdateBookingPolicyRequest(
        bool DepositEnabled,
        decimal? DepositPercent,
        int MaxPendingPerCustomer,
        int? MinDaysBetweenBookings,
        int? MaxBookingsPerMonth,
        bool AutoConfirmEnabled);
}
