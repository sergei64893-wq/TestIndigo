namespace Indigo.Domain.Entities;

/// <summary>
/// Сущность ценового тика с нормализованными данными
/// </summary>
public class PriceTick
{
    public Guid Id { get; set; }
    public required string Ticker { get; set; }
    public required decimal Price { get; set; }
    public required decimal Volume { get; set; }
    public required long Timestamp { get; set; } // Unix milliseconds
    public required string Source { get; set; } // Binance, Kraken, etc.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Hash для быстрого определения дубликатов
    /// </summary>
    public required string DuplicateCheckHash { get; set; }
    
    /// <summary>
    /// Raw JSON данные из источника
    /// </summary>
    public string? RawData { get; set; }
}

