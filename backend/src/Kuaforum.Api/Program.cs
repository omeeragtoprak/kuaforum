using Hangfire;
using Kuaforum.Api.Configuration;
using Kuaforum.Api.Endpoints;
using Kuaforum.Api.Middleware;
using Kuaforum.Application;
using Kuaforum.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Sentry: DSN ortam değişkeninden (Sentry__Dsn) gelir. Boş/unset DSN = SDK disabled
// (overhead yok). Sentry SDK null DSN'de exception fırlatır, "" ile disable et.
// AttachStacktrace + TracesSampleRate appsettings.Production.json'da config edilmiş.
var sentryDsnConfig = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsnConfig))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsnConfig;
        options.Environment = builder.Configuration["Sentry:Environment"] ?? builder.Environment.EnvironmentName;
        options.TracesSampleRate = builder.Configuration.GetValue<double?>("Sentry:TracesSampleRate") ?? 0;
        options.AttachStacktrace = builder.Configuration.GetValue<bool>("Sentry:AttachStacktrace");
        options.SendDefaultPii = false; // KVKK: PII Sentry'e gönderilmez
    });
}

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName();

    // Sentry: Serilog → Sentry breadcrumb/event köprüsü. DSN yoksa skip.
    var sentryDsn = context.Configuration["Sentry:Dsn"];
    if (!string.IsNullOrWhiteSpace(sentryDsn))
    {
        configuration.WriteTo.Sentry(o =>
        {
            o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
            o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
            o.Dsn = sentryDsn;
        });
    }
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKuaforumAuth(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var app = builder.Build();

// Production boot guard: appsettings.Production.json artık ${VAR} placeholder
// kullanmıyor (.NET bunları expand ETMEZ — önceden prod'a garbage connection
// string/secret ile sessizce boot alınıyordu). ConnectionStrings:Postgres/Redis
// (AddInfrastructure), Jwt:SigningKey (AddKuaforumAuth) ve Encryption:Key
// (AesEncryptionService ctor, AdminSeeder resolve edilirken eager construct
// edilir) kendi katmanlarında zaten throw ediyor. Otp:Pepper ise lazy
// kullanıldığı için boş kalırsa boot ÇÖKMEZ, sadece OTP hash'i pepper'sız
// (zayıflamış) üretilir — bunu burada açıkça fail-fast'e çeviriyoruz.
if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["Otp:Pepper"]))
{
    throw new InvalidOperationException(
        "Otp:Pepper is required in Production (set env var Otp__Pepper). " +
        "See infra/README.md 'Zorunlu environment değişkenleri' tablosu.");
}

// Caddy/Cloudflare arkasında çalışırken gerçek şema (https) ve client IP'yi
// X-Forwarded-Proto / X-Forwarded-For header'larından geri kazanır. Bu, en
// başta (routing/auth/HTTPS redirect'ten ÖNCE) uygulanmalı. KnownProxies/
// KnownNetworks kasıtlı olarak boş bırakıldı (= tüm forwarded header'lara
// güven) çünkü backend container'ı host'a publish EDİLMİYOR — sadece infra/
// docker-compose.prod.yml'deki internal network üzerinden Caddy erişebiliyor,
// dolayısıyla X-Forwarded-* spoof edecek dışarıdan bir aktör yok. Backend
// ileride host'a doğrudan publish edilirse burada KnownNetworks Caddy'nin
// compose subnet'ine daraltılmalı.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseCors();
app.UseRequestIdMiddleware();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }))
    .WithName("Health")
    .WithTags("System");

app.MapGet("/health/ready", async (
    Kuaforum.Infrastructure.Persistence.ApplicationDbContext db,
    StackExchange.Redis.IConnectionMultiplexer redis,
    CancellationToken ct) =>
{
    var checks = new Dictionary<string, object>();
    var allOk = true;

    // Postgres ping
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        sw.Stop();
        checks["postgres"] = new { status = "ok", latencyMs = sw.ElapsedMilliseconds };
    }
    catch (Exception ex)
    {
        checks["postgres"] = new { status = "fail", error = ex.Message };
        allOk = false;
    }

    // Redis ping
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pong = await redis.GetDatabase().PingAsync();
        sw.Stop();
        checks["redis"] = new { status = "ok", latencyMs = sw.ElapsedMilliseconds, pingMs = pong.TotalMilliseconds };
    }
    catch (Exception ex)
    {
        checks["redis"] = new { status = "fail", error = ex.Message };
        allOk = false;
    }

    var payload = new { status = allOk ? "ok" : "degraded", timestamp = DateTimeOffset.UtcNow, checks };
    return allOk ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
}).WithName("ReadinessProbe").WithTags("System");

// NOT: OpenAPI paketi net10.0 → net8.0 geçişinde söküldü, /openapi/v1.json yok.
// Kök route sadece "servis ayakta mı" kontrolü için /health'e yönlendiriyor.
app.MapGet("/", () => Results.Redirect("/health"))
    .ExcludeFromDescription();

app.MapAuthEndpoints(app.Environment);
app.MapUserEndpoints();
app.MapLegalEndpoints();
app.MapGeographyEndpoints();
app.MapBarberEndpoints();
app.MapAvailabilityEndpoints();
app.MapBookingEndpoints();
app.MapUploadsEndpoints();
app.MapPaymentEndpoints(app.Environment);
app.MapReviewEndpoints();
app.MapNotificationEndpoints();
app.MapDeviceTokenEndpoints();
app.MapAdminEndpoints();
app.MapBillingEndpoints(app.Environment);
app.MapWebhookEndpoints();
app.MapDevEndpoints(app.Environment);

// Hangfire dashboard + recurring jobs ancak Hangfire DI'a register edildiyse
// (Hangfire:Enabled config flag). Test ortamında Postgres yokken eager bağlanma
// yapmayı engellemek için DependencyInjection.cs flag'i kontrol ediyor.
var hangfireEnabled = builder.Configuration.GetValue<bool?>("Hangfire:Enabled") ?? true;
if (hangfireEnabled)
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        DashboardTitle = "Kuaforum Background Jobs",
        Authorization = [new HangfireDashboardAdminFilter()]
    });

    using var scope = app.Services.CreateScope();
    Kuaforum.Infrastructure.DependencyInjection.RegisterRecurringJobs(scope.ServiceProvider);
}

// Seed reference data (81 il + sample districts) on startup. Idempotent.
// Demo barber shops are seeded only when Seed:Demo=true (dev/staging only).
// Seed:ComprehensiveDemo=true ek olarak 15 şehir, ~50 müşteri, ~250 booking, payments,
// reviews, notifications üretir — visual demo + integration test için.
// Seed:RichDemo=true ek olarak (DemoBarberSeeder'ın 6 Erzurum salonunun üzerine) 7 yeni
// Erzurum salonu, dev quick-login "Customer" personası için her randevu durumu, yorum
// havuzu ve örnek faturalama üretir — "founder telefonda gezecek" demo deneyimi içindir.
var seedDemo = builder.Configuration.GetValue<bool>("Seed:Demo");
var seedComprehensive = builder.Configuration.GetValue<bool>("Seed:ComprehensiveDemo");
var seedRichDemo = builder.Configuration.GetValue<bool>("Seed:RichDemo");
await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
    app.Services, seedDemo, seedComprehensive, seedRichDemo, isProduction: app.Environment.IsProduction());

app.Run();

public partial class Program;

internal sealed class HangfireDashboardAdminFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        if (context is not Hangfire.Dashboard.AspNetCoreDashboardContext aspCtx) return false;
        var http = aspCtx.HttpContext;
        // Allow loopback connections (dev / Docker compose health checks) without auth.
        // IMPORTANT: check the actual socket RemoteIpAddress, NOT Request.Host — the Host
        // header is client-controlled (e.g. "Host: localhost") and trivially spoofable,
        // which previously let any remote caller bypass auth entirely.
        //
        // Behind Caddy (prod): app.UseForwardedHeaders() runs earlier in the pipeline
        // and rewrites RemoteIpAddress from X-Forwarded-For, so a real visitor hitting
        // /hangfire through Caddy sees their OWN public IP here (not loopback) — the
        // bypass correctly does NOT trigger and Admin auth is enforced. Only requests
        // that never went through Caddy's forwarding (e.g. a docker-compose healthcheck
        // hitting the backend container directly, or `dotnet run` locally) stay loopback.
        var remoteIp = http.Connection.RemoteIpAddress;
        if (remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp)) return true;
        var user = http.User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole("Admin");
    }
}
