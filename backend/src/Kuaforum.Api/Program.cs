using Hangfire;
using Kuaforum.Api.Configuration;
using Kuaforum.Api.Endpoints;
using Kuaforum.Api.Middleware;
using Kuaforum.Application;
using Kuaforum.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName();
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
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

app.MapGet("/", () => Results.Redirect("/openapi/v1.json"))
    .ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapGeographyEndpoints();
app.MapBarberEndpoints();
app.MapAvailabilityEndpoints();
app.MapBookingEndpoints();
app.MapPaymentEndpoints();
app.MapReviewEndpoints();
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
var seedDemo = builder.Configuration.GetValue<bool>("Seed:Demo");
await Kuaforum.Infrastructure.DependencyInjection.SeedReferenceDataAsync(app.Services, seedDemo);

app.Run();

public partial class Program;

internal sealed class HangfireDashboardAdminFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // TODO(Phase 2): wire admin role check via HttpContext.
        // Dev: dashboard erişimi açık; prod: Cloudflare Access /hangfire path'i kapatır.
        return true;
    }
}
