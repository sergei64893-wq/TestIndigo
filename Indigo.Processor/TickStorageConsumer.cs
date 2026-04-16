using Indigo.Domain.Entities;
using Indigo.Domain.ValueObjects;
using Indigo.Infrastructure.Interfaces;
using MassTransit;

public class TickStorageConsumer : IConsumer<Batch<NormalizedTick>>
{
    private readonly ITickRepository _repository;
    private readonly ILogger<TickStorageConsumer> _logger;

    public TickStorageConsumer(ITickRepository repository, ILogger<TickStorageConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Batch<NormalizedTick>> context)
    {
        var batch = context.Message;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("Processing batch of {Count} ticks", batch.Length);

        const int subBatchSize = 1000;
        var tasks = new List<Task>();

        for (int i = 0; i < batch.Length; i += subBatchSize)
        {
            var subBatch = batch.Skip(i).Take(subBatchSize);
            var entities = new List<PriceTick>(subBatch.Count());

            foreach (var msg in subBatch)
            {
                entities.Add(new PriceTick
                {
                    Id = Guid.CreateVersion7(),
                    Ticker = msg.Message.Ticker,
                    Price = msg.Message.Price,
                    Volume = msg.Message.Volume,
                    Timestamp = msg.Message.Timestamp,
                    Source = msg.Message.Source,
                    DuplicateCheckHash = msg.Message.DuplicateCheckHash,
                    RawData = msg.Message.RawData
                });
            }

            tasks.Add(_repository.BulkInsertAsync(entities));
        }

        await Task.WhenAll(tasks);

        // Сохраняем все изменения в БД
        await _repository.SaveChangesAsync();

        stopwatch.Stop();
        _logger.LogInformation("Processed batch of {Count} ticks in {ElapsedMilliseconds} ms", batch.Length, stopwatch.ElapsedMilliseconds);
    }
}
