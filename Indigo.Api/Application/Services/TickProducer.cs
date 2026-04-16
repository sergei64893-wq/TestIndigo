using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;
using MassTransit;
using System.Threading.Channels;

namespace Indigo.Application.Services;

/// <summary>
/// Сервис производства тиков в Kafka
/// </summary>
public class TickProducer : ITickProducer
{
    private readonly Channel<NormalizedTick> _tickChannel;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TickProducer> _logger;

    public TickProducer(
        Channel<NormalizedTick> tickChannel,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TickProducer> logger)
    {
        _tickChannel = tickChannel;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProduceTicksAsync(CancellationToken cancellationToken)
    {
        var batchSize = _configuration.GetValue("TickAggregation:BatchSize", 1000);
        var flushIntervalMs = _configuration.GetValue("TickAggregation:FlushIntervalMs", 5000);
        var topic = _configuration.GetValue("Kafka:Topic", "exchange-ticks");
        var batch = new List<NormalizedTick>(batchSize);

        // Получаем продюсер один раз
        using var scope = _scopeFactory.CreateScope();
        var tickProducer = scope.ServiceProvider.GetRequiredService<ITopicProducer<string, NormalizedTick>>();

        try
        {
            while (!cancellationToken.IsCancellationRequested || _tickChannel.Reader.Count > 0)
            {
                // Ждем появления данных или таймаута для сброса по времени
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(flushIntervalMs);

                try
                {
                    // WaitToReadAsync эффективнее, чем Task.WhenAny(Delay)
                    if (await _tickChannel.Reader.WaitToReadAsync(cts.Token))
                    {
                        while (batch.Count < batchSize && _tickChannel.Reader.TryRead(out var tick))
                        {
                            batch.Add(tick);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Выход по таймауту
                }

                if (batch.Count > 0)
                {
                    var sendToken = cancellationToken.IsCancellationRequested
                        ? CancellationToken.None
                        : cancellationToken;
                    await SendBatchAsync(tickProducer, batch, topic, sendToken);
                }

                // Если основной токен отменен и канал пуст — выходим
                if (cancellationToken.IsCancellationRequested && _tickChannel.Reader.Count == 0)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Critical error in ProduceTicksAsync");
        }
    }

    private async Task SendBatchAsync(ITopicProducer<string, NormalizedTick> producer, List<NormalizedTick> batch,
        string topic, CancellationToken ct)
    {
        try
        {
            await Task.WhenAll(batch.Select(tick => producer.Produce(topic, tick, ct)));

            _logger.LogDebug("Sent batch of {Count} ticks to {Topic}", batch.Count, topic);
        }
        finally
        {
            batch.Clear();
        }
    }
}
