using Hangfire;
using Kuaforum.Api.Configuration;
using Kuaforum.Api.Endpoints;
using Kuaforum.Api.Middleware;
using Kuaforum.Application;
using Kuaforum.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Sentry: DSN ortam değişkeninden gelir. Boş DSN = SDK disabled (overhead yok).
// Sentry SDK null DSN'de exception fırlatır, "" ile disable et.
// AttachStacktrace + TracesSampleRate appsettings.Production.json'da config edilmiş.
var sentryDsnConfig = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsnConfig) && !sentryDsnConfig.Contains("${SENTRY_DSN"))
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
    if (!string.IsNullOrWhiteSpace(sentryDsn) && !sentryDsn.Contains("${SENTRY_DSN"))
    {
        configuration.WriteTo.Sentry(o =>
        {
            o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
            o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
            o.Dsn = sentryDsn;
        });
    }
});

builder.Services.AddOpenApi();
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

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
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

app.MapGet("/", () => Results.Redirect("/openapi/v1.json"))
    .ExcludeFromDescription();

app.MapAuthEndpoints(app.Environment);
app.MapUserEndpoints();
app.MapGeographyEndpoints();
app.MapBarberEndpoints();
app.MapAvailabilityEndpoints();
app.MapBookingEndpoints();
app.MapUploadsEndpoints();
app.MapPaymentEndpoints();
app.MapReviewEndpoints();
app.MapNotificationEndpoints();
app.MapDeviceTokenEndpoints();
app.MapAdminEndpoints();

app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Kuaforum Background Jobs",
    Authorization = [new HangfireDashboardAdminFilter()]
});

using (var scope = app.Services.CreateScope())
{
    Kuaforum.Infrastructure.DependencyInjection.RegisterRecurringJobs(scope.ServiceProvider);
}

// Seed reference data (81 il + sample districts) on startup. Idempotent.
// Demo barber shops are seeded only when Seed:Demo=true (dev/staging only).
// Seed:ComprehensiveDemo=true ek olarak 15 şehir, ~50 müşteri, ~250 booking, payments,
// reviews, notifications üretir — visual demo + integration test için.
var seedDemo = builder.Configuration.GetValue<bool>("Seed:Demo");
var seedComprehensive = builder.Configuration.GetValue<bool>("Seed:ComprehensiveDemo");
await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(
    app.Services, seedDemo, seedComprehensive);

app.Run();

public partial class Program;

internal sealed class HangfireDashboardAdminFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        if (context is not Hangfire.Dashboard.AspNetCoreDashboardContext aspCtx) return false;
        var http = aspCtx.HttpContext;
        // Allow localhost (dev) without auth so Docker compose health checks work.
        if (http.Request.Host.Host is "localhost" or "127.0.0.1") return true;
        var user = http.User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole("Admin");
    }
}
