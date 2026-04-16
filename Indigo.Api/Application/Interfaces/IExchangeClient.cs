using System.Runtime.CompilerServices;
using Indigo.Domain.ValueObjects;

namespace Indigo.Application.Interfaces;

/// <summary>
/// Интерфейс для WebSocket клиента источника данных
/// </summary>
public interface IExchangeClient
{
    string SourceName { get; }
    bool IsConnected { get; }
    
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    IAsyncEnumerable<NormalizedTick> GetTicksStreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default);
    Task ReconnectAsync(CancellationToken cancellationToken = default);
}



