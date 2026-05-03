namespace Kuaforum.Domain.Modules.Identity;

public sealed class AuditLog
{
    private AuditLog() { }

    public long Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public AuditActorType ActorType { get; private set; }
    public string ActionType { get; private set; } = null!;
    public string ResourceType { get; private set; } = null!;
    public string ResourceId { get; private set; } = null!;
    public string? PayloadJson { get; private set; }
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }
    public string? RequestId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Create(
        Guid? actorUserId,
        AuditActorType actorType,
        string actionType,
        string resourceType,
        string resourceId,
        string? payloadJson = null,
        string? ip = null,
        string? userAgent = null,
        string? requestId = null)
    {
        return new AuditLog
        {
            ActorUserId = actorUserId,
            ActorType = actorType,
            ActionType = actionType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadJson = payloadJson,
            Ip = ip,
            UserAgent = userAgent,
            RequestId = requestId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

public enum AuditActorType
{
    User = 0,
    System = 1,
    Admin = 2,
}
