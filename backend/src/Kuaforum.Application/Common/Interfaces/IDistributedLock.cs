namespace Kuaforum.Application.Common.Interfaces;

public interface IDistributedLock
{
    Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default);
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    string Key { get; }
}
