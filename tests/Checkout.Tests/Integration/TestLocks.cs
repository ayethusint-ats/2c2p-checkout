using Checkout.Application.Abstractions;

namespace Checkout.Tests.Integration;

/// <summary>Every caller "gets" the lock: as if the lock gave no protection at all.</summary>
internal sealed class AlwaysGrantsLock : IDistributedLock
{
    public Task<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<ILockHandle?>(new NoOpHandle());

    private sealed class NoOpHandle : ILockHandle
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>The lock store is down: every attempt throws.</summary>
internal sealed class ThrowingLock : IDistributedLock
{
    public Task<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct) =>
        throw new InvalidOperationException("Redis is unavailable (simulated).");
}
