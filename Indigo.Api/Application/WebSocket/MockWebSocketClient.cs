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
        
        // ✅ Для достижения 1000+ тиков/сек нужно генерировать больше тиков за раз
        // 4 клиента × 250 тиков/сек = 1000 тиков/сек в сумме
        // Каждый клиент должен генерировать 250-300 тиков/сек
        // При задержке 10-50ms = нужно 2.5-15 тиков в батче, генерируем 50-100
        
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            tickCount++;
            
            // ✅ Генерируем больше тиков за один цикл (50-150 вместо 2-6)
            // Это даёт нам 1000-3000 тиков/сек при 4 клиентах
            var ticksInBatch = _random.Next(50, 150);
            
            for (int i = 0; i < ticksInBatch; i++)
            {
                var ticker = tickers[_random.Next(tickers.Length)];
                var basePrice = basePrices[ticker];
                
                // ✅ Имитируем движение цены (±5%)
                var priceVariation = (decimal)(_random.NextDouble() - 0.5) * 2 * (basePrice * 0.05m);
                var price = basePrice + priceVariation;
                
                var volume = (decimal)(_random.Next(10, 1000) + _random.NextDouble());
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                var duplicateHash = $"{ticker}_{price:F8}_{timestamp}_{SourceName}";
                
                yield return new NormalizedTick(
                    Ticker: ticker,
                    Price: Math.Max(0.01m, price),  // ✅ Цена не может быть отрицательной
                    Volume: volume,
                    Timestamp: timestamp,
                    Source: SourceName,
                    DuplicateCheckHash: duplicateHash,
                    RawData: $"{{\"symbol\":\"{ticker}\",\"price\":{price},\"qty\":{volume},\"time\":{timestamp}}}"
                );
            }
            
            // ✅ Уменьшаем задержку чтобы достичь 250+ тиков/сек на этом клиенте
            // 50-100 тиков за 10-50ms = 1000-5000 тиков/сек на одном клиенте
            await Task.Delay(_random.Next(10, 50), cancellationToken);
        }
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

