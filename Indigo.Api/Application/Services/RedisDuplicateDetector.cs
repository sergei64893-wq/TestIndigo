using Indigo.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Indigo.Application.Services;

/// <summary>
/// Детектор дубликатов с Redis кэшем
/// </summary>
public class RedisDuplicateDetector : IDuplicateDetector
{
    private readonly IDistributedCache _cache;
    private const string CacheKeyPrefix = "duplicate_";

    public RedisDuplicateDetector(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> IsDuplicateAsync(string duplicateCheckHash)
    {
        var key = CacheKeyPrefix + duplicateCheckHash;
        var value = await _cache.GetStringAsync(key);
        return value != null;
    }

    public async Task RegisterTickAsync(string duplicateCheckHash, TimeSpan ttl)
    {
        var key = CacheKeyPrefix + duplicateCheckHash;
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };
        await _cache.SetStringAsync(key, "true", options);
    }
}
