using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Indigo.Application.Interfaces;
using Indigo.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Indigo.Application.WebSocket;

/// <summary>
/// Абстрактный базовый класс для WebSocket клиентов бирж
/// </summary>
public abstract class BaseExchangeWebSocketClient : IExchangeClient
{
    protected readonly ILogger Logger;
    protected readonly string WebSocketUrl;
    protected volatile System.Net.WebSockets.ClientWebSocket? WebSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly ObjectPool<MemoryStream> _streamPool;
    private readonly object _webSocketLock = new object();
    private readonly ILogger _logger;  // ✅ Для логирования в методах

    public abstract string SourceName { get; }
    public bool IsConnected
    {
        get
        {
            lock (_webSocketLock)
            {
                return WebSocket?.State == WebSocketState.Open;
            }
        }
    }

    protected BaseExchangeWebSocketClient(ILogger logger, string webSocketUrl, ObjectPool<MemoryStream> streamPool)
    {
        Logger = logger;
        _logger = logger;
        WebSocketUrl = webSocketUrl;
        _streamPool = streamPool;
    }

    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_webSocketLock)
            {
                WebSocket = new System.Net.WebSockets.ClientWebSocket();
            }
            
            // Настройка буферов сокета (опционально, но полезно для больших сообщений)
            // WebSocket.Options.SetBuffer(65536, 65536);

            await WebSocket.ConnectAsync(new Uri(WebSocketUrl), _cancellationTokenSource.Token);
            Logger.LogInformation("{Source}: WebSocket connected", SourceName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Source}: Failed to connect", SourceName);
            throw;
        }
    }

    public virtual async Task DisconnectAsync()
    {
        System.Net.WebSockets.ClientWebSocket? localWebSocket;
        lock (_webSocketLock)
        {
            localWebSocket = WebSocket;
            WebSocket = null;  // Обнуляем под lock
        }

        if (localWebSocket is not null)
        {
            try
            {
                if (localWebSocket.State == WebSocketState.Open)
                {
                    await localWebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing connection",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{Source}: Error during CloseAsync", SourceName);
            }
            finally
            {
                localWebSocket.Dispose();
            }
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    public virtual async IAsyncEnumerable<NormalizedTick> GetTicksStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        System.Net.WebSockets.ClientWebSocket? localWebSocket;
        lock (_webSocketLock)
        {
            if (WebSocket is null || WebSocket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException($"{SourceName}: WebSocket is not connected");
            }
            localWebSocket = WebSocket;  // Захватываем локальную копию под lock
        }

        var buffer = _bufferPool.Rent(65536);
        var watch = new Stopwatch();
        const int maxStreamCapacityBeforeDispose = 1024 * 1024;  // 1MB - если больше, то dispose

        try
        {
            while (localWebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var messageStream = _streamPool.Get();
                WebSocketReceiveResult result;

                watch.Restart();
                NormalizedTick? normalizedTick = null;
                try
                {
                    do
                    {
                        result = await localWebSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Logger.LogWarning("{Source}: WebSocket received close frame", SourceName);
                            yield break;
                        }

                        messageStream.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage);  // ✅ Проверяем, что сообщение полное

                    // ✅ Обработка собранного сообщения
                    try
                    {
                        if (messageStream.TryGetBuffer(out var segment))
                        {
                            normalizedTick = NormalizeMessageAsync(segment.AsSpan());
                        }
                        else
                        {
                            normalizedTick = NormalizeMessageAsync(messageStream.ToArray());
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "{Source}: Error normalizing message. Stream size: {Size}",
                            SourceName, messageStream.Length);
                        continue;
                    }

                    watch.Stop();

                    if (normalizedTick is not null)
                    {
                        if (watch.ElapsedMilliseconds > 10)
                        {
                            Logger.LogWarning(
                                "{Source}: Slow processing {TotalTime}ms. Size: {Size} bytes",
                                SourceName, watch.ElapsedMilliseconds, messageStream.Length);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogError(ex, "{Source}: Error receiving fragments", SourceName);
                    throw;
                }

                if (normalizedTick is not null)
                {
                    yield return normalizedTick;
                }

                // ✅ Возвращаем MemoryStream в pool или dispose если слишком большой
                if (messageStream.Capacity > maxStreamCapacityBeforeDispose)
                {
                    messageStream.Dispose();
                    _logger.LogWarning("{Source}: MemoryStream capacity {Capacity}MB exceeded limit, disposed",
                        SourceName, messageStream.Capacity / (1024 * 1024));
                }
                else
                {
                    messageStream.SetLength(0);
                    _streamPool.Return(messageStream);
                }
            }
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }

    public virtual async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        await ConnectAsync(cancellationToken);
    }

    protected abstract NormalizedTick? NormalizeMessageAsync(ReadOnlySpan<byte> bytes);

    protected static string GenerateDuplicateHash(string ticker, decimal price, long timestamp, string source)
    {
        return $"{ticker}_{price}_{timestamp}_{source}";
    }
}
