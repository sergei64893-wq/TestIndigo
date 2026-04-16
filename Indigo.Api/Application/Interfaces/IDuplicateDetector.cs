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
}

