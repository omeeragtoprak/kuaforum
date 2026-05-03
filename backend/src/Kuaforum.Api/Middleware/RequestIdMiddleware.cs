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

        using (Serilog.Context.LogContext.PushProperty("RequestId", requestId))
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
