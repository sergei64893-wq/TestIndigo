using Indigo.Application.Interfaces;

namespace Indigo.Application.Services;

/// <summary>
/// Главный сервис агрегации и обработки тиков (координатор)
/// </summary>
public class TickAggregationService : IHostedService
{
    private readonly ILogger<TickAggregationService> _logger;
    private readonly IExchangeClient[] _exchangeClients;
    private readonly ITickProcessor _tickProcessor;
    private readonly ITickProducer _tickProducer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _aggregationTask;
    private Task? _producerTask;

    public TickAggregationService(
        ILogger<TickAggregationService> logger,
        IEnumerable<IExchangeClient> exchangeClients,
        ITickProcessor tickProcessor,
        ITickProducer tickProducer)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToArray();
        _tickProcessor = tickProcessor;
        _tickProducer = tickProducer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _producerTask = _tickProducer.ProduceTicksAsync(_cancellationTokenSource.Token);
            _aggregationTask = _tickProcessor.ProcessTicksAsync(_cancellationTokenSource.Token);

            _logger.LogInformation("TickAggregationService started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting TickAggregationService");
            throw;
        }
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TickAggregationService stopping...");

        _cancellationTokenSource?.Cancel();

        if (_aggregationTask is not null)
        {
            try
            {
                await _aggregationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        if (_producerTask is not null)
        {
            try
            {
                await _producerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // Отключаем все клиенты
        foreach (var client in _exchangeClients)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(client.SourceName, "Error disconnecting client", ex);
            }
        }

        _cancellationTokenSource?.Dispose();
        _logger.LogInformation("TickAggregationService stopped");
    }
}