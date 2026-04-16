using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;
using System.Threading.Channels;

namespace Indigo.Application.Services;

/// <summary>
/// Сервис обработки тиков от клиентов
/// </summary>
public class TickProcessor : ITickProcessor
{
    private readonly IExchangeClient[] _exchangeClients;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly Channel<NormalizedTick> _tickChannel;
    private readonly ILogger<TickProcessor> _logger;

    public TickProcessor(
        IEnumerable<IExchangeClient> exchangeClients,
        IDuplicateDetector duplicateDetector,
        Channel<NormalizedTick> tickChannel,
        ILogger<TickProcessor> logger)
    {
        _exchangeClients = exchangeClients.ToArray();
        _duplicateDetector = duplicateDetector;
        _tickChannel = tickChannel;
        _logger = logger;
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
            // Expected during shutdown
        }
    }

    private async Task ProcessClientStreamAsync(IExchangeClient client, CancellationToken cancellationToken)
    {
        int reconnectAttempts = 0;
        const int maxReconnectAttempts = 10;
        var count = 0;
        var logInterval = 1000; // Логируем каждые 1000 тиков
        while (!cancellationToken.IsCancellationRequested && reconnectAttempts < maxReconnectAttempts)
        {
            try
            {
                await client.ConnectAsync(cancellationToken);
                reconnectAttempts = 0;

                await foreach (var normalizedTick in client.GetTicksStreamAsync(cancellationToken))
                {
                    // Проверяем во внешнем детекторе (Redis/DB)
                    if (await _duplicateDetector.IsDuplicateAsync(normalizedTick.DuplicateCheckHash))
                    {
                        continue;
                    }

                    await _tickChannel.Writer.WriteAsync(normalizedTick, cancellationToken);
                    count++;

                    // Логируем каждые logInterval тиков для снижения нагрузки
                    if (count % logInterval == 0)
                    {
                        _logger.LogInformation("{Count} ticks processed from {Source}", count, client.SourceName);
                    }

                    await _duplicateDetector.RegisterTickAsync(normalizedTick.DuplicateCheckHash,
                        TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reconnectAttempts++;
                _logger.LogWarning(ex, "Reconnection attempt {Attempt} for {Source}", reconnectAttempts,
                    client.SourceName);
            }
        }
    }
}
