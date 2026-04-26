namespace Indigo.Application.Interfaces;

/// <summary>
/// Интерфейс для детектирования дубликатов
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>
    /// Проверяет, является ли тик дубликатом
    /// </summary>
    Task<bool> IsDuplicateAsync(string duplicateCheckHash);
    
    /// <summary>
    /// Регистрирует тик в кэше дубликатов
    /// </summary>
    Task RegisterTickAsync(string duplicateCheckHash, TimeSpan ttl);

    /// <summary>
    /// Проверяет батч хэшей на дубликаты
    /// </summary>
    Task<bool[]> IsDuplicateBatchAsync(IEnumerable<string> hashes);

    /// <summary>
    /// Регистрирует батч хэшей
    /// </summary>
    Task RegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl);

    /// <summary>
    /// Атомарно проверяет и регистрирует батч хэшей, возвращая, были ли они новыми
    /// </summary>
    Task<bool[]> CheckAndRegisterBatchAsync(IEnumerable<string> hashes, TimeSpan ttl);
}
