namespace Kuaforum.Api.Middleware;

public sealed class RequestIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Request-Id";

    public async Task Invoke(HttpContext context)
    {
        var requestId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.CreateVersion7().ToString();

        context.Response.Headers[HeaderName] = requestId;
        context.Items[HeaderName] = requestId;

        // Enrich Serilog context with RequestId AND (when authenticated) UserId.
        var sub = context.User.FindFirst("sub")?.Value;
        var role = context.User.FindFirst("role")?.Value;

        using (Serilog.Context.LogContext.PushProperty("RequestId", requestId))
        using (sub is not null ? Serilog.Context.LogContext.PushProperty("UserId", sub) : null)
        using (role is not null ? Serilog.Context.LogContext.PushProperty("UserRole", role) : null)
        {
            await next(context);
        }
    }
}

public static class RequestIdMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestIdMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<RequestIdMiddleware>();
}
