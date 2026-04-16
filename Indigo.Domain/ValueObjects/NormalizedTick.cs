namespace Indigo.Domain.ValueObjects;

/// <summary>
/// Значение нормализованного тика - Domain Value Object
/// </summary>
public record NormalizedTick(
    string Ticker,
    decimal Price,
    decimal Volume,
    long Timestamp, // Unix milliseconds
    string Source,
    string DuplicateCheckHash,
    string? RawData = null
);