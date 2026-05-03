using System.Text.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Infrastructure.Auth;

public sealed class AuditLogger(
    IApplicationDbContext db,
    IRequestContext requestContext) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task RecordAsync(
        string actionType,
        string resourceType,
        string resourceId,
        Guid? actorUserId = null,
        object? payload = null,
        CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var actor = actorUserId ?? requestContext.UserId;
        var actorType = actor is null ? AuditActorType.System : AuditActorType.User;

        var payloadJson = payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions);

        var entry = AuditLog.Create(
            actorUserId: actor,
            actorType: actorType,
            actionType: actionType,
            resourceType: resourceType,
            resourceId: resourceId,
            payloadJson: payloadJson,
            ip: requestContext.Ip,
            userAgent: requestContext.UserAgent,
            requestId: requestContext.RequestId);

        await ctx.Set<AuditLog>().AddAsync(entry, ct);
    }
}
