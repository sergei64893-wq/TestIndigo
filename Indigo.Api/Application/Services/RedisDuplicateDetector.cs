using Indigo.Application.Interfaces;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System.Buffers;

namespace Indigo.Application.Services;

/// <summary>
/// Детектор дубликатов с Redis кэшем. Полностью потокобезопасен и оптимизирован для 1000+ тиков/сек.
/// </summary>
public class RedisDuplicateDetector : IDuplicateDetector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDuplicateDetector> _logger;
    private const string CacheKeyPrefix = "duplicate_";

    public RedisDuplicateDetector(IConnectionMultiplexer redis, ILogger<RedisDuplicateDetector> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> IsDuplicateAsync(string duplicateCheckHash)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeyPrefix + duplicateCheckHash;
            var exists = await db.KeyExistsAsync(key);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking duplicate for hash {Hash}", duplicateCheckHash);
            throw;
        }
    }

    public async Task RegisterTickAsync(string duplicateCheckHash, TimeSpan ttl)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeyPrefix + duplicateCheckHash;
            await db.StringSetAsync(key, "1", ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering duplicate for hash {Hash}", duplicateCheckHash);
            throw;
        }
    }

    /// <summary>
    /// Проверяет батч хэшей на дубликаты, используя MGET (1 запрос вместо N)
    /// </summary>
    public async Task<bool[]> IsDuplicateBatchAsync(IEnumerable<string> hashes)
    {
        try
        {
            var hashList = hashes.ToList();
            if (hashList.Count == 0)
                return Array.Empty<bool>();

            var db = _redis.GetDatabase();
            var keys = ArrayPool<RedisKey>.Shared.Rent(hashList.Count);

            try
            {
                // ✅ Конвертируем хэши в Redis ключи
                for (int i = 0; i < hashList.Count; i++)
                {
                    keys[i] = CacheKeyPrefix + hashList[i];
                }

                // ✅ MGET - один запрос для всех ключей вместо N GET
                var values = await db.StringGetAsync(keys);
                var results = ArrayPool<bool>.Shared.Rent(values.Length);

                try
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        results[i] = values[i].HasValue;
                    }

                    // Возвращаем копию, поскольку caller может модифицировать
                    return results.AsSpan(0, values.Length).ToArray();
                }
                finally
                {
                    ArrayPool<bool>.Shared.Return(results);
                }
            }
            finally
            {
                ArrayPool<RedisKey>.Shared.Return(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking duplicate batch");
            throw;
        }
    }

    /// <summary>
    /// Регистрирует батч хэшей, используя Lua script для атомарности
    /// </summary>
    public async Task RegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl)
    {
        var hashList = hashes as List<string> ?? hashes.ToList();
        try
        {
            if (hashList.Count == 0)
                return;

            var db = _redis.GetDatabase();
            var expiry = (long)ttl.TotalSeconds;
            var keys = new RedisKey[hashList.Count];

            for (int i = 0; i < hashList.Count; i++)
            {
                keys[i] = CacheKeyPrefix + hashList[i];
            }

            // ✅ Lua скрипт: регистрирует все ключи атомарно
            var script = @"
                for i, key in ipairs(KEYS) do
                    redis.call('SETEX', key, ARGV[1], ARGV[2])
                end
                return 1";

            var args = new RedisValue[] { expiry, "1" };
            await db.ScriptEvaluateAsync(script, keys, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering batch with {Count} hashes", 
                hashList.Count);
            throw;
        }
    }

    /// <summary>
    /// Атомарно проверяет и регистрирует батч хэшей, возвращая массив bool.
    /// true = новый, false = дубликат. Использует Lua script для полной атомарности.
    /// </summary>
    public async Task<bool[]> CheckAndRegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl)
    {
        var hashList = hashes as List<string> ?? hashes.ToList();
        try
        {
            if (hashList.Count == 0)
                return Array.Empty<bool>();

            var db = _redis.GetDatabase();
            var expiry = (long)ttl.TotalSeconds;
            var keys = new RedisKey[hashList.Count];

            for (int i = 0; i < hashList.Count; i++)
            {
                keys[i] = CacheKeyPrefix + hashList[i];
            }

            // ✅ Lua скрипт: проверяет наличие и регистрирует атомарно
            // Возвращает 1 если новый, 0 если дубликат - БЕЗ TOCTOU!
            var script = @"
                local results = {}
                for i, key in ipairs(KEYS) do
                    if redis.call('EXISTS', key) == 0 then
                        redis.call('SETEX', key, ARGV[1], ARGV[2])
                        results[i] = 1
                    else
                        results[i] = 0
                    end
                end
                return results";

            var args = new RedisValue[] { expiry, "1" };
            var result = (RedisValue[])await db.ScriptEvaluateAsync(script, keys, args);

            var boolResults = new bool[result.Length];
            for (int i = 0; i < result.Length; i++)
            {
                boolResults[i] = result[i] == 1;
            }

            return boolResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckAndRegisterBatchAsync with {Count} hashes", 
                hashList.Count);
            throw;
        }
    }
}
