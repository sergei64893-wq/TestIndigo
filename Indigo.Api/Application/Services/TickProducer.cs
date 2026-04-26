using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;
using MassTransit;
using System.Threading.Channels;
using System.Diagnostics;
using Microsoft.Extensions.ObjectPool;

namespace Indigo.Application.Services;

/// <summary>
/// Сервис производства тиков в Kafka. Оптимизирован для 1000+ тиков/сек с правильным батчингом и таймером.
/// </summary>
public class TickProducer : ITickProducer
{
    private readonly Channel<NormalizedTick> _tickChannel;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TickProducer> _logger;
    private readonly ObjectPool<List<NormalizedTick>> _batchPool;

    public TickProducer(
        Channel<NormalizedTick> tickChannel,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TickProducer> logger,
        ObjectPool<List<NormalizedTick>> batchPool)
    {
        _tickChannel = tickChannel;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchPool = batchPool;
    }

    public async Task ProduceTicksAsync(CancellationToken cancellationToken)
    {
        var batchSize = _configuration.GetValue("TickAggregation:BatchSize", 1000);
        var flushIntervalMs = _configuration.GetValue("TickAggregation:FlushIntervalMs", 100);
        var topic = _configuration.GetValue("Kafka:Topic", "exchange-ticks");

        var batch = _batchPool.Get();
        var lastFlushTime = Stopwatch.StartNew();
        long batchesSent = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested || _tickChannel.Reader.Count > 0)
            {
                var remainingMs = Math.Max(0, flushIntervalMs - (int)lastFlushTime.ElapsedMilliseconds);

                // ✅ Правильный таймер с отдельным CancellationTokenSource
                bool hasData = false;
                try
                {
                    if (remainingMs > 0)
                    {
                        // Ждем с таймаутом
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(remainingMs);
                        hasData = await _tickChannel.Reader.WaitToReadAsync(timeoutCts.Token);
                    }
                    else
                    {
                        // Не ждем, сразу проверяем
                        hasData = _tickChannel.Reader.Count > 0;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // ✅ Timeout сработал - пора отправить батч
                    hasData = false;
                }

                // ✅ Читаем все доступные данные в батч
                if (hasData)
                {
                    while (batch.Count < batchSize && _tickChannel.Reader.TryRead(out var tick))
                    {
                        batch.Add(tick);
                    }
                }

                // ✅ Отправляем если батч полный ИЛИ прошло достаточно времени и есть данные
                bool shouldFlush = batch.Count >= batchSize ||
                                    (lastFlushTime.ElapsedMilliseconds >= flushIntervalMs && batch.Count > 0);

                if (shouldFlush && batch.Count > 0)
                {
                    // ✅ Получаем scoped producer для каждой отправки батча
                    using var scope = _scopeFactory.CreateScope();
                    var producer = scope.ServiceProvider.GetRequiredService<ITopicProducer<string, NormalizedTick>>();
                    
                    await SendBatchAsync(producer, batch, topic, cancellationToken);
                    batchesSent++;
                    lastFlushTime.Restart();
                }
            }
        }
        finally
        {
            // ✅ Отправить оставшиеся данные перед выключением
            if (batch.Count > 0)
            {
                try
                {
                    // ✅ Получаем scoped producer для финальной отправки
                    using var scope = _scopeFactory.CreateScope();
                    var producer = scope.ServiceProvider.GetRequiredService<ITopicProducer<string, NormalizedTick>>();
                    
                    await SendBatchAsync(producer, batch, topic, cancellationToken);
                    batchesSent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending final batch");
                }
            }

            batch.Clear();
            _batchPool.Return(batch);
            _logger.LogInformation("ProduceTicksAsync completed. Total batches sent: {Count}", batchesSent);
        }
    }

    private async Task SendBatchAsync(ITopicProducer<string, NormalizedTick> producer,
        List<NormalizedTick> batch, string topic, CancellationToken ct)
    {
        const int maxRetries = 3;
        int attempt = 0;
        var sw = Stopwatch.StartNew();

        while (attempt < maxRetries)
        {
            try
            {
                // ✅ Отправляем все тики батча параллельно
                var tasks = new List<Task>(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                {
                    tasks.Add(producer.Produce(topic, batch[i], ct));
                }

                await Task.WhenAll(tasks);

                sw.Stop();
                _logger.LogInformation("Sent batch of {Count} ticks to {Topic} in {ElapsedMs}ms",
                    batch.Count, topic, sw.ElapsedMilliseconds);

                batch.Clear();
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                attempt++;
                _logger.LogWarning(ex, "Kafka produce failed, attempt {Attempt}/{MaxRetries}",
                    attempt, maxRetries);

                // ✅ Exponential backoff с максимумом 500ms
                var backoffMs = Math.Min(attempt * 100, 500);
                await Task.Delay(TimeSpan.FromMilliseconds(backoffMs), ct);
            }
            catch (Exception ex) when (attempt == maxRetries - 1)
            {
                attempt++;
                _logger.LogError(ex, "Final attempt failed for batch of {Count} ticks", batch.Count);
            }
        }

        // ✅ После всех попыток - логируем критическую ошибку
        _logger.LogCritical("Failed to produce batch of {Count} ticks after {MaxRetries} attempts",
            batch.Count, maxRetries);

        batch.Clear();
    }
}
