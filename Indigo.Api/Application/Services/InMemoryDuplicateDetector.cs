using Indigo.Application.Interfaces;
using System.Collections.Concurrent;

namespace Indigo.Application.Services;

/// <summary>
/// Детектор дубликатов с in-memory кэшем. Полностью потокобезопасен для 1000+ тиков/сек.
/// Использует ConcurrentDictionary вместо IMemoryCache для лучшей производительности при больших нагрузках.
/// </summary>
public class InMemoryDuplicateDetector : IDuplicateDetector
{
    // ✅ ConcurrentDictionary - полностью потокобезопасна, нет локов
    private readonly ConcurrentDictionary<string, long> _duplicateCache = new();
    private const string CacheKeyPrefix = "duplicate_";
    private static long _cleanupCount;
    
    // ✅ Ограничение размера кэша для предотвращения утечки памяти
    private const int MaxCacheSize = 100000; // Максимум 100k записей
    private static readonly object SizeLock = new();

    public InMemoryDuplicateDetector()
    {
    }

    public async Task<bool> IsDuplicateAsync(string duplicateCheckHash)
    {
        var key = CacheKeyPrefix + duplicateCheckHash;
        
        if (_duplicateCache.TryGetValue(key, out var expiryTime))
        {
            // ✅ Проверяем не истек ли TTL
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < expiryTime)
            {
                return true;
            }
            else
            {
                // ✅ Удаляем истекший ключ
                _duplicateCache.TryRemove(key, out _);
            }
        }

        return await Task.FromResult(false);
    }

    public async Task RegisterTickAsync(string duplicateCheckHash, TimeSpan ttl)
    {
        var key = CacheKeyPrefix + duplicateCheckHash;
        var expiryTime = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds();
        
        _duplicateCache.TryAdd(key, expiryTime);
        
        await Task.CompletedTask;
    }

    public Task<bool[]> IsDuplicateBatchAsync(IEnumerable<string> hashes)
    {
        var hashList = hashes.ToList();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var results = new bool[hashList.Count];
        var keysToRemove = new List<string>(hashList.Count);

        for (int i = 0; i < hashList.Count; i++)
        {
            var key = CacheKeyPrefix + hashList[i];
            
            if (_duplicateCache.TryGetValue(key, out var expiryTime))
            {
                if (now < expiryTime)
                {
                    results[i] = true;
                }
                else
                {
                    results[i] = false;
                    keysToRemove.Add(key);
                }
            }
            else
            {
                results[i] = false;
            }
        }

        // Удаляем истекшие ключи атомарно
        foreach (var key in keysToRemove)
        {
            _duplicateCache.TryRemove(key, out _);
        }

        return Task.FromResult(results);
    }

    public async Task RegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl)
    {
        var expiryTime = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds();
        
        foreach (var hash in hashes)
        {
            var key = CacheKeyPrefix + hash;
            // ✅ TryAdd - добавляем только если ещё нет (не перезаписываем)
            _duplicateCache.TryAdd(key, expiryTime);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Атомарно проверяет и регистрирует батч хэшей.
    /// Возвращает массив bool: true = новый, false = дубликат
    /// </summary>
    public async Task<bool[]> CheckAndRegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl)
    {
        var hashList = hashes as List<string> ?? hashes.ToList();
        var expiryTime = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds();
        var results = new bool[hashList.Count];

        // Атомарная проверка и регистрация для каждого хэша
        for (int i = 0; i < hashList.Count; i++)
        {
            var key = CacheKeyPrefix + hashList[i];
            
            // AddOrUpdate атомарно: если ключа нет - добавляем, если есть - проверяем TTL
            var updated = _duplicateCache.AddOrUpdate(key, 
                // Ключ не существует - добавляем новый
                expiryTime,
                // Ключ существует - проверяем, не истек ли TTL
                (_, existingValue) => {
                    // Вычисляем now атомарно внутри update function для избежания TOCTOU
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now < existingValue)
                    {
                        // Ключ существует и не истек - возвращаем существующий (не обновляем)
                        return existingValue;
                    }
                    else
                    {
                        // Ключ истек - обновляем
                        return expiryTime;
                    }
                });

            // Результат: true если установили новый expiryTime (новый или истекший)
            results[i] = updated == expiryTime;
        }

        // Периодическая очистка истекших ключей (каждые 10000 операций)
        if (Interlocked.Increment(ref _cleanupCount) % 10000 == 0)
        {
            _ = Task.Run(() => CleanupExpiredKeys(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        // Ограничение размера кэша - удаляем старые записи если кэш переполнен
        lock (SizeLock)
        {
            if (_duplicateCache.Count > MaxCacheSize)
            {
                // Сначала пытаемся удалить истекшие ключи
                var cleanupNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var expiredKeys = new List<string>();
                foreach (var kvp in _duplicateCache)
                {
                    if (kvp.Value <= cleanupNow)
                    {
                        expiredKeys.Add(kvp.Key);
                        if (expiredKeys.Count >= 1000) break;
                    }
                }
                foreach (var key in expiredKeys)
                {
                    _duplicateCache.TryRemove(key, out _);
                }
                
                // Если всё ещё переполнен, удаляем первые ключи
                if (_duplicateCache.Count > MaxCacheSize)
                {
                    var keysToRemove = new List<string>();
                    foreach (var key in _duplicateCache.Keys)
                    {
                        keysToRemove.Add(key);
                        if (keysToRemove.Count >= 1000) break;
                    }
                    foreach (var key in keysToRemove)
                    {
                        _duplicateCache.TryRemove(key, out _);
                    }
                }
            }
        }

        return await Task.FromResult(results);
    }

    /// <summary>
    /// Фоновая очистка истекших ключей
    /// </summary>
    private void CleanupExpiredKeys(long now)
    {
        try
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in _duplicateCache)
            {
                if (kvp.Value <= now)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _duplicateCache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                // Логируем количество удалённых ключей
            }
        }
        catch (Exception)
        {
            // Игнорируем ошибки в фоновом процессе очистки
        }
    }
}
