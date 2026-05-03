using System.Security.Claims;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Kuaforum.Infrastructure.Auth;

public sealed class HttpContextRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    private const string RequestIdHeader = "X-Request-Id";

    public Guid? UserId
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Ip => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? RequestId
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null) return null;
            if (ctx.Items.TryGetValue(RequestIdHeader, out var v) && v is string s) return s;
            return ctx.Response.Headers[RequestIdHeader].FirstOrDefault();
        }
    }
}
