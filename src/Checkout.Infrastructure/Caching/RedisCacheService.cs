using System.Text.Json;
using System.Text.Json.Serialization;
using Checkout.Application.Abstractions;
using StackExchange.Redis;

namespace Checkout.Infrastructure.Caching;

internal sealed class RedisCacheService(IConnectionMultiplexer redis) : ICacheService
{
    // camelCase + enums as names, so values are readable in redis-cli and survive enum reordering.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await redis.GetDatabase().StringGetAsync(key);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl) =>
        redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), ttl);

    public Task RemoveAsync(string key) =>
        redis.GetDatabase().KeyDeleteAsync(key);
}
