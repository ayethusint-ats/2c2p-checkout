namespace Checkout.Application.Abstractions;

/// <summary>A lock shared by all API instances (e.g. in Redis).</summary>
public interface IDistributedLock
{
    /// <summary>
    /// Tries once, without waiting. Returns null if someone else holds the lock.
    /// Throws if the lock store itself is unavailable.
    /// </summary>
    /// <param name="ttl">The lock expires on its own after this, even if never released.</param>
    Task<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

/// <summary>
/// Disposing releases the lock, but only if this handle still owns it
/// (after the TTL expires, someone else may hold it). Never throws.
/// </summary>
public interface ILockHandle : IAsyncDisposable;
