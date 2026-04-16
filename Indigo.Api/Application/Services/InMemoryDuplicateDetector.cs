using Indigo.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Indigo.Application.Services;

/// <summary>
/// Детектор дубликатов с in-memory кэшем
/// </summary>
public class InMemoryDuplicateDetector : IDuplicateDetector
{
    private readonly IMemoryCache _cache;
    private const string CacheKeyPrefix = "duplicate_";

    public InMemoryDuplicateDetector(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> IsDuplicateAsync(string duplicateCheckHash)
    {
        return await Task.FromResult(_cache.TryGetValue(CacheKeyPrefix + duplicateCheckHash, out _));
    }

    public async Task RegisterTickAsync(string duplicateCheckHash, TimeSpan ttl)
    {
        var key = CacheKeyPrefix + duplicateCheckHash;
        _cache.Set(key, true, ttl);
        
        await Task.CompletedTask;
    }
}


