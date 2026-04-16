using System.Buffers.Text;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Indigo.Domain.Exceptions;
using Indigo.Domain.ValueObjects;

namespace Indigo.Application.WebSocket;

/// <summary>
/// WebSocket клиент для Kraken с реальным подключением
/// Kraken API: https://docs.kraken.com/websockets/
/// </summary>
public class KrakenWebSocketClient : BaseExchangeWebSocketClient
{
    private readonly string[] _pairs = { "XBT/USD", "ETH/USD", "XRP/USD" };

    public override string SourceName => "Kraken";

    public KrakenWebSocketClient(ILogger<KrakenWebSocketClient> logger)
        : base(logger, "wss://ws.kraken.com")
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await base.ConnectAsync(cancellationToken);

        // После подключения отправляем subscription message
        if (WebSocket?.State == WebSocketState.Open)
        {
            await SubscribeToTradesAsync(cancellationToken);
        }
    }

    private async Task SubscribeToTradesAsync(CancellationToken cancellationToken)
    {
        if (WebSocket is null || WebSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"{SourceName}: WebSocket is not connected for subscription");
        }

        var subscriptionMessage = new
        {
            @event = "subscribe",
            pair = _pairs,
            subscription = new
            {
                name = "trade"
            }
        };

        var json = JsonSerializer.Serialize(subscriptionMessage);
        var buffer = Encoding.UTF8.GetBytes(json);

        await WebSocket.SendAsync(
            new ArraySegment<byte>(buffer),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

        Logger.LogInformation($"{SourceName}: Sent subscription for pairs: {string.Join(", ", _pairs)}");
    }

    protected override NormalizedTick NormalizeMessageAsync(ReadOnlySpan<byte>  bytes)
    {
        var reader = new Utf8JsonReader(bytes);

        string? ticker = null;
        decimal price = 0;
        decimal volume = 0;
        long timestamp = 0;
        bool isTrade = false;

        try
        {
            // Kraken trade — это массив. Читаем последовательно.
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) return null;

            int arrayIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (arrayIndex == 1) // Массив сделок
                {
                    // Заходим внутрь массива сделок
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        // Нам нужна последняя сделка. Kraken часто присылает пачку сделок в одном массиве.
                        // Для упрощения и скорости читаем первую/последнюю без создания List.
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                // Внутри массива сделки: [price, volume, time, side, type, misc]
                                int fieldIndex = 0;
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (fieldIndex == 0)
                                        price = decimal.Parse(reader.ValueSpan, provider: CultureInfo.InvariantCulture);
                                    if (fieldIndex == 1)
                                        volume = decimal.Parse(reader.ValueSpan,
                                            provider: CultureInfo.InvariantCulture);
                                    if (fieldIndex == 2)
                                    {
                                        // Парсим double-timestamp "1534614057.321597" напрямую из Span
                                        if (Utf8Parser.TryParse(reader.ValueSpan, out double t, out _))
                                            timestamp = (long)(t * 1000);
                                    }

                                    fieldIndex++;
                                }
                            }
                        }
                    }
                }
                else if (arrayIndex == 2) // Имя канала
                {
                    if (reader.GetString() == "trade") isTrade = true;
                }
                else if (arrayIndex == 3) // Тикер (XBT/USD)
                {
                    ReadOnlySpan<byte> pairSpan = reader.ValueSpan;
                    // Оптимизируем удаление '/': выделяем память на стеке
                    Span<char> tickerChars = stackalloc char[pairSpan.Length];
                    int charCount = Encoding.UTF8.GetChars(pairSpan, tickerChars);

                    int written = 0;
                    Span<char> finalTicker = stackalloc char[charCount];
                    for (int i = 0; i < charCount; i++)
                    {
                        if (tickerChars[i] != '/') finalTicker[written++] = tickerChars[i];
                    }

                    ticker = new string(finalTicker.Slice(0, written));
                }

                arrayIndex++;
            }

            if (!isTrade || ticker == null) return null;

            var duplicateHash = GenerateDuplicateHash(ticker, price, timestamp, SourceName);

            return new NormalizedTick(
                Ticker: ticker,
                Price: price,
                Volume: volume,
                Timestamp: timestamp,
                Source: SourceName,
                DuplicateCheckHash: duplicateHash,
                RawData: Encoding.UTF8.GetString(bytes)
            );
        }
        catch (Exception ex)
        {
            throw new InvalidTickDataException($"Fast parse failed: {ex.Message}", ex);
        }
    }
}