using Kuaforum.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Api.Endpoints;

/// <summary>
/// Development-only by default. Route, <see cref="DevToolsGate.IsEnabled"/> false iken hiç map
/// EDİLMEZ (AuthEndpoints "dev-login" ile birebir aynı kalıp) — bu durumda path fiziksel olarak
/// yok (404, auth kontrolüne bile ulaşmaz). Development dışında yalnız <c>DevTools:Enabled=true</c>
/// ile açılır (Koyeb free-tier test deploy'u — bkz. docs/DEPLOY-FREE-TIER.md). Ayrıca Admin
/// authorization ZORUNLU (defense-in-depth — dev-login "Admin" persona'sıyla token alınır, bkz.
/// DevLoginRole.Admin) + isteğe bağlı DevTools:AccessKey header kontrolü.
///
/// Bu grup yalnız <see cref="Kuaforum.Infrastructure.Persistence.Seeding.DemoDataResetter"/>'ın
/// (TRUNCATE — coğrafya/plan tanımları hariç) manuel/on-demand tetikleyicisidir. Otomatik
/// bayatlama tespiti (uygulama her açılışında) zaten DependencyInjection.SeedReferenceDataAsync
/// içinde çalışır — bu endpoint, restart YAPMADAN "şimdi tazele" isteyen dev/founder içindir.
/// </summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment env, IConfiguration rootConfig)
    {
        if (!DevToolsGate.IsEnabled(env, rootConfig)) return app;

        var group = app.MapGroup("/v1/dev").WithTags("Dev").RequireAuthorization("Admin");

        group.MapPost("/reseed-demo", async (IServiceProvider sp, IConfiguration config, HttpContext ctx, CancellationToken ct) =>
        {
            var accessKeyError = DevToolsGate.CheckAccessKey(ctx, config);
            if (accessKeyError is not null) return accessKeyError;

            var seedDemo = config.GetValue<bool>("Seed:Demo");
            var seedComprehensive = config.GetValue<bool>("Seed:ComprehensiveDemo");
            var seedRich = config.GetValue<bool>("Seed:RichDemo");
            if (!seedRich)
            {
                return Results.Problem(
                    "Seed:RichDemo=false — bu endpoint yalnız RichDemo açıkken anlamlı (owner/admin faturalama + bugünkü randevu demo verisi RichDemoSeeder'a ait).",
                    statusCode: 400);
            }

            await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
                sp, seedDemo, seedComprehensive, seedRich, isProduction: false, forceReset: true, ct: ct);

            return Results.Ok(new { message = "Demo verisi tazelendi (bugünün tarihine göre yeniden üretildi)." });
        }).WithName("ReseedDemoData");

        return app;
    }
}
