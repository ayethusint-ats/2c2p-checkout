using Checkout.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Checkout.Infrastructure.Caching;

/// <summary>
/// Single-instance Redis lock: SET key token NX PX ttl to acquire, compare-and-delete (Lua) to release.
/// </summary>
internal sealed class RedisDistributedLock(IConnectionMultiplexer redis, ILogger<RedisDistributedLock> logger)
    : IDistributedLock
{
    // Atomic in Redis: delete only if the lock still holds our token, so an expired lock that
    // another request has since acquired is never released by us.
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
          return redis.call('del', KEYS[1])
        else
          return 0
        end
        """;

    public async Task<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A fresh random value per acquisition identifies this owner.
        var token = Guid.NewGuid().ToString("N");

        // SET NX with expiry: "create if absent" and "expire after ttl" in one atomic command.
        var acquired = await redis.GetDatabase().StringSetAsync(key, token, ttl, When.NotExists);

        return acquired ? new Handle(redis, key, token, logger) : null;
    }

    private sealed class Handle(IConnectionMultiplexer redis, string key, string token, ILogger logger) : ILockHandle
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
                return;

            try
            {
                await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [key], [token]);
            }
            catch (Exception ex)
            {
                // Never throw from release: the TTL frees the lock anyway.
                logger.LogWarning(ex, "Could not release lock {LockKey}; it will expire after its TTL", key);
            }
        }
    }
}
