using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Conquer.Core.Net
{
    /// <summary>
    /// <see cref="IOnlineLink"/> over a WebSocket to the game server. Frames are sent one at a time in order,
    /// incoming ones are size-capped, and a heartbeat keeps the server's idle timer from dropping a player
    /// who is just thinking. Events fire through <c>post</c> so the window gets them on its own thread.
    /// </summary>
    public sealed class WebSocketLink : IOnlineLink
    {
        const int MaxFrameBytes = SnapshotCodec.MaxBytes + 1024;
        static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(15);

        readonly WebSocket _socket;
        readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly Action<Action> _post;
        int _closed;

        public event Action<byte[]> Received;
        public event Action<string> Closed;

        WebSocketLink(WebSocket socket, Action<Action> post)
        {
            _socket = socket;
            _post = post ?? (a => a());
        }

        /// <summary>Runs the link over a socket that is already open (tests use the in-memory server's).</summary>
        public static WebSocketLink Over(WebSocket socket, Action<Action> post = null)
        {
            var link = new WebSocketLink(socket, post);
            link.Run();
            return link;
        }

        void Run()
        {
            _ = SendLoopAsync();
            _ = ReceiveLoopAsync();
            _ = HeartbeatAsync();
        }

        /// <summary>
        /// Turns what a person typed into a server address: "example.com" becomes "wss://example.com/ws", and a
        /// bare local address gets plain ws. Returns null if it can't be a WebSocket address.
        /// </summary>
        public static Uri ParseAddress(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (!text.Contains("://"))
            {
                bool local = text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || text.StartsWith("127.") ||
                             text.StartsWith("192.168.") || text.StartsWith("10.");
                text = (local ? "ws://" : "wss://") + text;
            }
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri uri)) return null;
            if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "ws", Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri;
            if (uri.Scheme == "https") uri = new UriBuilder(uri) { Scheme = "wss", Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri;
            if (uri.Scheme != "ws" && uri.Scheme != "wss") return null;
            if (uri.AbsolutePath == "/") uri = new UriBuilder(uri) { Path = "/ws" }.Uri;
            return uri;
        }

        /// <summary>Connects, or throws with a message a person can read.</summary>
        public static async Task<WebSocketLink> ConnectAsync(Uri address, Action<Action> post, TimeSpan timeout)
        {
            var socket = new ClientWebSocket();
            using (var cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    await socket.ConnectAsync(address, cts.Token);
                }
                catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is System.Net.Http.HttpRequestException)
                {
                    socket.Dispose();
                    throw new InvalidOperationException(cts.IsCancellationRequested
                        ? "The server didn't answer in time."
                        : "Couldn't reach the server. Check the address and your connection.", e);
                }
            }
            return Over(socket, post);
        }

        public void Send(byte[] frame)
        {
            if (Volatile.Read(ref _closed) == 0) _outbox.Writer.TryWrite(frame);
        }

        public void Close() => Shutdown("You left the room.");

        async Task SendLoopAsync()
        {
            try
            {
                await foreach (byte[] frame in _outbox.Reader.ReadAllAsync(_cts.Token))
                    await _socket.SendAsync(frame, WebSocketMessageType.Binary, true, _cts.Token);
            }
            catch (Exception e) when (IsDisconnect(e))
            {
                Shutdown("The connection to the server was lost.");
            }
        }

        async Task ReceiveLoopAsync()
        {
            var buffer = new byte[MaxFrameBytes];
            string reason = "The connection to the server was lost.";
            try
            {
                while (true)
                {
                    int count = 0;
                    ValueWebSocketReceiveResult result;
                    do
                    {
                        if (count == buffer.Length)
                        {
                            reason = "The server sent something too large.";
                            return;
                        }
                        result = await _socket.ReceiveAsync(buffer.AsMemory(count), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            reason = "The server closed the connection.";
                            return;
                        }
                        count += result.Count;
                    } while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Binary || count == 0) continue;
                    byte[] frame = buffer.AsSpan(0, count).ToArray();
                    _post(() => Received?.Invoke(frame));
                }
            }
            catch (Exception e) when (IsDisconnect(e))
            {
            }
            finally
            {
                Shutdown(reason);
            }
        }

        async Task HeartbeatAsync()
        {
            byte[] beat = { Protocol.Heartbeat };
            try
            {
                while (true)
                {
                    await Task.Delay(HeartbeatEvery, _cts.Token);
                    Send(beat);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        void Shutdown(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            _outbox.Writer.TryComplete();
            _ = CloseSocketAsync();
            _post(() => Closed?.Invoke(reason));
        }

        async Task CloseSocketAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
                }
            }
            catch (Exception e) when (IsDisconnect(e))
            {
            }
            finally
            {
                _cts.Cancel();
                _socket.Dispose();
            }
        }

        static bool IsDisconnect(Exception e) =>
            e is OperationCanceledException || e is WebSocketException || e is ObjectDisposedException || e is System.IO.IOException;
    }
}
