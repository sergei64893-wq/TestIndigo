using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace Indigo.Application.Services;

/// <summary>
/// Сервис обработки тиков от клиентов. Полностью потокобезопасен, без race conditions.
/// Каждый клиент имеет свой batch (нет гонок данных).
/// </summary>
public class TickProcessor : ITickProcessor
{
    private readonly IExchangeClient[] _exchangeClients;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly Channel<NormalizedTick> _tickChannel;
    private readonly ILogger<TickProcessor> _logger;
    private readonly ObjectPool<List<NormalizedTick>> _batchPool;
    private readonly ObjectPool<List<string>> _hashPool;

    // ✅ Каждый клиент имеет свой batch - нет race conditions между клиентами
    private readonly ConcurrentDictionary<string, ClientBatchState> _clientBatches = new();

    private const int BATCH_SIZE = 500; // ✅ Увеличен для 1000+ тиков/сек
    private const long BATCH_TIME_MS = 100;

    private class ClientBatchState
    {
        public List<NormalizedTick> Batch { get; set; } = new(BATCH_SIZE);
        public List<string> Hashes { get; set; } = new(BATCH_SIZE);
        public long BatchStartTimeMs { get; set; }
        public int ProcessedCount;
        public readonly object Lock = new(); // ✅ Lock для синхронизации
        
        // ✅ Кэшируем timestamp для ShouldFlushBatch
        private long _lastTimestampCheck;
        private const long TimestampCacheMs = 10; // Кэшируем на 10ms
        
        public bool ShouldFlushBatchCached(int batchSize, long batchTimeMs)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Используем кэшированный timestamp если прошло мало времени
            if (now - _lastTimestampCheck < TimestampCacheMs)
            {
                now = _lastTimestampCheck;
            }
            else
            {
                _lastTimestampCheck = now;
            }
            
            if (Batch.Count >= batchSize)
                return true;

            return (now - BatchStartTimeMs) >= batchTimeMs;
        }
    }

    public TickProcessor(
        IEnumerable<IExchangeClient> exchangeClients,
        IDuplicateDetector duplicateDetector,
        Channel<NormalizedTick> tickChannel,
        ILogger<TickProcessor> logger,
        ObjectPool<List<NormalizedTick>> batchPool,
        ObjectPool<List<string>> hashPool)
    {
        _exchangeClients = exchangeClients.ToArray();
        _duplicateDetector = duplicateDetector;
        _tickChannel = tickChannel;
        _logger = logger;
        _batchPool = batchPool;
        _hashPool = hashPool;
    }

    public async Task ProcessTicksAsync(CancellationToken cancellationToken)
    {
        var tasks = new Task[_exchangeClients.Length];
        for (int i = 0; i < _exchangeClients.Length; i++)
        {
            tasks[i] = ProcessClientStreamAsync(_exchangeClients[i], cancellationToken);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // ✅ Ожидаемо при выключении
        }
    }

    private async Task ProcessClientStreamAsync(IExchangeClient client, CancellationToken cancellationToken)
    {
        var clientKey = client.SourceName;
        int reconnectAttempts = 0;
        const int maxReconnectAttempts = 10;

        // ✅ Инициализируем состояние батча для этого клиента
        var batchState = new ClientBatchState
        {
            BatchStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _clientBatches[clientKey] = batchState;

        try
        {
            while (!cancellationToken.IsCancellationRequested && reconnectAttempts < maxReconnectAttempts)
            {
                try
                {
                    await client.ConnectAsync(cancellationToken);
                    reconnectAttempts = 0;

                    await foreach (var normalizedTick in client.GetTicksStreamAsync(cancellationToken))
                    {
                        lock (batchState.Lock) // ✅ Синхронизируем доступ к batchState
                        {
                            // ✅ Добавляем тик в батч этого клиента (потокобезопасно)
                            batchState.Batch.Add(normalizedTick);
                            batchState.Hashes.Add(normalizedTick.DuplicateCheckHash);

                            // ✅ Проверяем нужно ли отправлять батч
                            if (batchState.ShouldFlushBatchCached(BATCH_SIZE, BATCH_TIME_MS))
                            {
                                // Выходим из lock перед асинхронной операцией
                            }
                            else
                            {
                                continue;
                            }
                        }
                        
                        // Выполняем flush вне lock
                        await FlushBatchAsync(clientKey, batchState, cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reconnectAttempts++;
                    _logger.LogWarning(ex, "Reconnection attempt {Attempt} for {Source}",
                        reconnectAttempts, client.SourceName);

                    if (reconnectAttempts < maxReconnectAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(reconnectAttempts * 2, 30)), cancellationToken);
                    }
                }
            }
        }
        finally
        {
            // ✅ Отправляем оставшиеся тики перед выключением
            if (batchState.Batch.Count > 0)
            {
                try
                {
                    await FlushBatchAsync(clientKey, batchState, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error flushing final batch for {Source}", client.SourceName);
                }
            }

            _clientBatches.TryRemove(clientKey, out _);
        }
    }

    /// <summary>
    /// Отправляет батч: проверяет дубликаты и пишет новые тики в channel
    /// </summary>
    private async Task FlushBatchAsync(string clientKey, ClientBatchState batchState, CancellationToken cancellationToken)
    {
        List<NormalizedTick> batchToSend;
        List<string> hashesToCheck;
        
        // ✅ Захватываем данные под lock
        lock (batchState.Lock)
        {
            if (batchState.Batch.Count == 0)
                return;
                
            // Копируем данные для обработки вне lock
            batchToSend = _batchPool.Get();
            hashesToCheck = _hashPool.Get();
            foreach (var item in batchState.Batch)
            {
                batchToSend.Add(item);
            }
            foreach (var hash in batchState.Hashes)
            {
                hashesToCheck.Add(hash);
            }
            
            // Очищаем batch под lock
            batchState.Batch.Clear();
            batchState.Hashes.Clear();
            batchState.BatchStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        try
        {
            // ✅ Атомарная проверка и регистрация батча (Redis Lua script - NO TOCTOU!)
            var isNew = await _duplicateDetector.CheckAndRegisterBatchAsync(
                hashesToCheck,
                TimeSpan.FromSeconds(5)
            );

            int sentCount = 0;
            // ✅ Параллельная отправка тиков в канал для лучшей производительности
            var writeTasks = new List<Task>(batchToSend.Count);
            for (int i = 0; i < batchToSend.Count; i++)
            {
                if (isNew[i])
                {
                    writeTasks.Add(_tickChannel.Writer.WriteAsync(batchToSend[i], cancellationToken).AsTask());
                    sentCount++;
                }
            }
            
            // Ждем завершения всех записей параллельно
            await Task.WhenAll(writeTasks);

            Interlocked.Add(ref batchState.ProcessedCount, batchToSend.Count);
            
            _logger.LogInformation("{Source}: Flushed batch of {Total} ticks ({New} new, {Dupes} dupes, total: {Processed})",
                clientKey, batchToSend.Count, sentCount, batchToSend.Count - sentCount, batchState.ProcessedCount);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning("Channel closed for {Source}", clientKey);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing batch for {Source}", clientKey);
            throw;
        }
        finally
        {
            _batchPool.Return(batchToSend);
            _hashPool.Return(hashesToCheck);
        }
    }
}
