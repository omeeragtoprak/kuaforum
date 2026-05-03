using Kuaforum.Application.Common.Interfaces;

namespace Kuaforum.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
