namespace Kuaforum.Application.Common.Interfaces;

public interface IRequestContext
{
    Guid? UserId { get; }
    string? Ip { get; }
    string? UserAgent { get; }
    string? RequestId { get; }
}
