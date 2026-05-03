namespace Kuaforum.Application.Common.Interfaces;

public interface IAuditLogger
{
    Task RecordAsync(
        string actionType,
        string resourceType,
        string resourceId,
        Guid? actorUserId = null,
        object? payload = null,
        CancellationToken ct = default);
}
