using System.Runtime.CompilerServices;
using System.Text;
using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;

namespace Indigo.Application.WebSocket;

/// <summary>
/// Mock WebSocket клиент для разработки и тестирования
/// Генерирует синтетические данные тиков
/// </summary>
public class MockWebSocketClient : IExchangeClient
{
    private readonly ILogger _logger;
    private readonly string _source;
    private readonly Random _random;
    private CancellationTokenSource? _cancellationTokenSource;

    public string SourceName => _source;
    public bool IsConnected { get; private set; }

    public MockWebSocketClient(ILogger logger, string source = "MockExchange")
    {
        _logger = logger;
        _source = source;
        _random = new Random();
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsConnected = true;
            _logger.LogInformation($"{SourceName}: Mock WebSocket connected");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{SourceName}: Failed to connect");
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        try
        {
            IsConnected = false;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _logger.LogInformation($"{SourceName}: Mock WebSocket disconnected");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{SourceName}: Failed to disconnect");
            throw;
        }
    }

    public async IAsyncEnumerable<NormalizedTick> GetTicksStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"{SourceName}: WebSocket is not connected");
        }

        var tickers = new[] { "BTC", "ETH", "ADA", "DOGE", "XRP" };
        var basePrices = new Dictionary<string, decimal>
        {
            ["BTC"] = 50000,
            ["ETH"] = 3000,
            ["ADA"] = 1.0m,
            ["DOGE"] = 0.15m,
            ["XRP"] = 2.5m
        };

        var tickCount = 0;
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            tickCount++;
            
            // Генерируем 2-5 тиков за один цикл для имитации реального потока
            var ticksInBatch = _random.Next(2, 6);
            for (int i = 0; i < ticksInBatch; i++)
            {
                var ticker = tickers[_random.Next(tickers.Length)];
                var basePrice = basePrices[ticker];
                
                // Имитируем движение цены (±5%)
                var priceVariation = (decimal)(_random.NextDouble() - 0.5) * 2 * (basePrice * 0.05m);
                var price = basePrice + priceVariation;
                
                var volume = (decimal)(_random.Next(10, 1000) + _random.NextDouble());
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                var duplicateHash = $"{ticker}_{price}_{timestamp}_{SourceName}";
                
                yield return new NormalizedTick(
                    Ticker: ticker,
                    Price: Math.Max(0.01m, price), // Цена не может быть отрицательной
                    Volume: volume,
                    Timestamp: timestamp,
                    Source: SourceName,
                    DuplicateCheckHash: duplicateHash,
                    RawData: $"{{\"symbol\":\"{ticker}\",\"price\":{price},\"qty\":{volume},\"time\":{timestamp}}}"
                );
            }
            
            // Имитируем задержку между пакетами (10-100ms для 50-100 тиков/сек суммарно)
            await Task.Delay(_random.Next(10, 100), cancellationToken);
        }
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

