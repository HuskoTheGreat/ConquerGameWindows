using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Conquer.Server
{
    /// <summary>
    /// One client's WebSocket. Outgoing frames go through a short bounded queue drained by its own send loop, so
    /// a slow or stalled client can never hold up its room: if the queue fills, the client is dropped.
    /// </summary>
    public sealed class Connection
    {
        static long _lastId;

        readonly WebSocket _socket;
        readonly Channel<byte[]> _outbox;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        volatile Room _room;
        volatile bool _joining;

        public ulong Id { get; } = (ulong)Interlocked.Increment(ref _lastId);
        public string Ip { get; }
        public CancellationToken Aborted => _cts.Token;

        /// <summary>Set by the room loop once a join succeeds; read by the receive loop.</summary>
        public Room Room
        {
            get => _room;
            internal set => _room = value;
        }

        /// <summary>A create/join is in flight, so further ones are ignored until it's answered.</summary>
        public bool Joining
        {
            get => _joining;
            internal set => _joining = value;
        }

        public Connection(WebSocket socket, string ip, int queueLength)
        {
            _socket = socket;
            Ip = ip;
            _outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(queueLength)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails instead of silently dropping
            });
        }

        /// <summary>Queues a frame. Never blocks. A client too slow to keep up is disconnected.</summary>
        public void Send(byte[] frame)
        {
            if (!_outbox.Writer.TryWrite(frame)) Abort();
        }

        /// <summary>Sends whatever is queued, then closes.</summary>
        public void Close() => _outbox.Writer.TryComplete();

        public void Abort()
        {
            _outbox.Writer.TryComplete();
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        public async Task SendLoopAsync()
        {
            try
            {
                await foreach (byte[] frame in _outbox.Reader.ReadAllAsync(_cts.Token))
                    await _socket.SendAsync(frame, WebSocketMessageType.Binary, true, _cts.Token);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
            catch (Exception e) when (e is OperationCanceledException || e is WebSocketException || e is ObjectDisposedException ||
                                       e is System.IO.IOException)
            {
            }
            finally
            {
                // Ends the receive loop too, which reports the disconnect to the room.
                try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }
}
