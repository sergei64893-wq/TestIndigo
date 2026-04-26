using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Indigo.Domain.Exceptions;
using Indigo.Domain.ValueObjects;
using Microsoft.Extensions.ObjectPool;

namespace Indigo.Application.WebSocket;

/// <summary>
/// WebSocket клиент для Binance с реальным подключением
/// Binance API: https://binance-docs.github.io/apidocs/spot/en/#trade-streams
/// </summary>
public class BinanceWebSocketClient : BaseExchangeWebSocketClient
{
    public override string SourceName => "Binance";

    public BinanceWebSocketClient(ILogger<BinanceWebSocketClient> logger, string pair, ObjectPool<MemoryStream> streamPool)
        : base(logger, $"wss://stream.binance.com:9443/ws/{pair}@trade", streamPool)
    {
    }

    protected override NormalizedTick? NormalizeMessageAsync(ReadOnlySpan<byte> bytes)
    {
        // Работаем с байтами напрямую (UTF8), чтобы избежать создания промежуточных строк
        var reader = new Utf8JsonReader(bytes);

        string? ticker = null;
        decimal price = 0;
        decimal volume = 0;
        long timestamp = 0;
        bool isTrade = false;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "e":
                    if (reader.GetString() != "trade") return null;
                    isTrade = true;
                    break;
                case "s": ticker = reader.GetString(); break;
                case "p":
                    // Парсим decimal напрямую из буфера UTF8 без создания строки
                    if (!Utf8Parser.TryParse(reader.ValueSpan, out price, out int _))
                    {
                        // Если в JSON цена в кавычках (строка), пробуем распарсить содержимое
                        if (decimal.TryParse(reader.GetString(), CultureInfo.InvariantCulture, out var p)) price = p;
                    }

                    break;
                case "q":
                    if (!Utf8Parser.TryParse(reader.ValueSpan, out volume, out int _))
                    {
                        if (decimal.TryParse(reader.GetString(), CultureInfo.InvariantCulture, out var v)) volume = v;
                    }

                    break;
                case "T": timestamp = reader.GetInt64(); break;
            }
        }

        if (!isTrade || ticker == null) return null;

        // Вместо SHA256 (тяжелая аллокация), используем быстрый детерминированный HashCode
        var duplicateHash = $"{ticker}_{price}_{timestamp}_{SourceName}";

        // Возвращаем ValueTask или готовый объект без Task.FromResult (лишняя аллокация Task)
        return new NormalizedTick(
            Ticker: ticker,
            Price: price,
            Volume: volume,
            Timestamp: timestamp,
            Source: SourceName,
            DuplicateCheckHash: duplicateHash,
            RawData: null  // Не аллоцировать строку, экономим память
        );
    }
}