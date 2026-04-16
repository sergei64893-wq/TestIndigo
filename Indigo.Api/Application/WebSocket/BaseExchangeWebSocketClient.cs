using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;

namespace Indigo.Application.WebSocket;

/// <summary>
/// Абстрактный базовый класс для WebSocket клиентов бирж
/// </summary>
public abstract class BaseExchangeWebSocketClient : IExchangeClient
{
    protected readonly ILogger Logger;
    protected readonly string WebSocketUrl;
    protected System.Net.WebSockets.ClientWebSocket? WebSocket;
    private CancellationTokenSource? _cancellationTokenSource;

    public abstract string SourceName { get; }
    public bool IsConnected => WebSocket?.State == WebSocketState.Open;

    protected BaseExchangeWebSocketClient(ILogger logger, string webSocketUrl)
    {
        Logger = logger;
        WebSocketUrl = webSocketUrl;
    }

    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            WebSocket = new System.Net.WebSockets.ClientWebSocket();
            
            await WebSocket.ConnectAsync(new Uri(WebSocketUrl), _cancellationTokenSource.Token);
            Logger.LogInformation($"{SourceName}: WebSocket connected");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"{SourceName}: Failed to connect");
            throw;
        }
    }

    public virtual async Task DisconnectAsync()
    {
        if (WebSocket is not null && WebSocket.State == WebSocketState.Open)
        {
            try
            {
                await WebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing connection",
                    CancellationToken.None);
            }
            finally
            {
                WebSocket.Dispose();
                WebSocket = null;
            }
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    public virtual async IAsyncEnumerable<NormalizedTick> GetTicksStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {

        if (WebSocket is null || WebSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"{SourceName}: WebSocket is not connected");
        }

        var buffer = new byte[65536];

        var totalCount = 0;
        while (WebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            totalCount++;
            long readMs = 0;
            WebSocketReceiveResult result;
            var watch = new Stopwatch();
            watch.Start();
            try
            {
                result = await WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);
                readMs = watch.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"{SourceName}: Error receiving message");
                throw;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            NormalizedTick? normalizedTick = null;
            try
            {
                normalizedTick = NormalizeMessageAsync(buffer.AsSpan(0,result.Count));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"{SourceName}: Error normalizing message");
                throw;
            }

            watch.Stop();
            if (normalizedTick is not null)
            {
                if (watch.ElapsedMilliseconds > 3)
                {
                    Logger.LogWarning($"{readMs} ms read; {watch.ElapsedMilliseconds}ms to process message from {SourceName}");
                }
                yield return normalizedTick;
            }
        }
    }

    public virtual async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        await ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Абстрактный метод для нормализации сообщения конкретной биржи
    /// </summary>
    protected abstract NormalizedTick NormalizeMessageAsync(ReadOnlySpan<byte>  bytes);

    protected static string GenerateDuplicateHash(string ticker, decimal price, long timestamp, string source)
    {
        return $"{ticker}_{price}_{timestamp}_{source}";
    }
}








