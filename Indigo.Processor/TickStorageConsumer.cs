using Indigo.Domain.Entities;
using Indigo.Domain.ValueObjects;
using Indigo.Infrastructure.Interfaces;
using MassTransit;
using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

/// <summary>
/// Consumer для сохранения тиков в БД. Полностью параллельный, без семафора.
/// Каждый батч имеет свой DbContext через scoped dependency injection.
/// </summary>
public class TickStorageConsumer : IConsumer<Batch<NormalizedTick>>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<TickStorageConsumer> _logger;
    private static long _totalProcessed = 0;

    public TickStorageConsumer(IServiceScopeFactory serviceScopeFactory, ILogger<TickStorageConsumer> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Batch<NormalizedTick>> context)
    {
        // ✅ Каждый батч получает свой DbContext через scoped injection
        // Нет семафора - может быть параллельная обработка
        using var scope = _serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITickRepository>();
        var objectPool = scope.ServiceProvider.GetRequiredService<ObjectPool<List<PriceTick>>>();

        var batch = context.Message;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Processing batch of {Count} ticks", batch.Length);

        try
        {
            // ✅ Обрабатываем подбатчами чтобы не держать память долго
            const int subBatchSize = 500;
            int processedInBatch = 0;

            for (int i = 0; i < batch.Length; i += subBatchSize)
            {
                var endIndex = Math.Min(i + subBatchSize, batch.Length);
                var subBatchLength = endIndex - i;

                var entities = objectPool.Get();
                try
                {
                    // ✅ Конвертируем без ToList() - прямо в список
                    for (int j = i; j < endIndex; j++)
                    {
                        var msg = batch[j];
                        entities.Add(new PriceTick
                        {
                            Id = Guid.CreateVersion7(),
                            Ticker = msg.Message.Ticker,
                            Price = msg.Message.Price,
                            Volume = msg.Message.Volume,
                            Timestamp = msg.Message.Timestamp,
                            Source = msg.Message.Source,
                            DuplicateCheckHash = msg.Message.DuplicateCheckHash,
                            RawData = null  // ✅ Не сохраняем RawData - экономим память
                        });
                    }

                    // ✅ Сохраняем сразу, не ждем конца батча
                    await repository.AddRangeAsync(entities);

                    processedInBatch += subBatchLength;

                    _logger.LogDebug("Saved subbatch of {Count} ticks ({Processed}/{Total})",
                        subBatchLength, processedInBatch, batch.Length);
                }
                finally
                {
                    entities.Clear();
                    objectPool.Return(entities);
                }
            }

            // ✅ Сохраняем все изменения в БД одним вызовом
            await repository.SaveChangesAsync();

            stopwatch.Stop();

            Interlocked.Add(ref _totalProcessed, batch.Length);
            _logger.LogInformation(
                "Processed batch of {Count} ticks in {ElapsedMilliseconds}ms (Total: {Total})",
                batch.Length, stopwatch.ElapsedMilliseconds, _totalProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch of {Count} ticks", batch.Length);
            throw;
        }
    }
}
