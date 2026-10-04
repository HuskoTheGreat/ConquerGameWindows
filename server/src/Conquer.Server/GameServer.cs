using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Core.Net;
using Microsoft.Extensions.Logging;

namespace Conquer.Server
{
    /// <summary>
    /// Accepts WebSocket clients and runs each one's receive loop: size cap, idle timeout, frame rate limit,
    /// and routing to the client's room. Game logic never runs here; it all happens on the room's loop.
    /// </summary>
    public sealed class GameServer
    {
        const int StrikesBeforeKick = 12;

        readonly ServerOptions _options;
        readonly RoomRegistry _rooms;
        readonly Func<double> _clock;
        readonly ILogger<GameServer> _log;
        readonly CountLimiter _connections;
        readonly KeyedRateLimiter _joinAttempts;

        public GameServer(ServerOptions options, RoomRegistry rooms, Func<double> clock, ILogger<GameServer> log)
        {
            _options = options;
            _rooms = rooms;
            _clock = clock;
            _log = log;
            _connections = new CountLimiter(options.MaxConnectionsPerIp, options.MaxConnections);
            _joinAttempts = new KeyedRateLimiter(options.JoinAttemptsPerMinute / 60.0, options.JoinAttemptBurst, clock);
        }

        public int ConnectionCount => _connections.Total;

        /// <summary>Checked before the WebSocket upgrade, so refused clients cost almost nothing.</summary>
        public bool TryAdmit(string ip) => _connections.TryAcquire(ip);

        public void Release(string ip) => _connections.Release(ip);

        public async Task RunAsync(WebSocket socket, string ip)
        {
            var conn = new Connection(socket, ip, _options.SendQueueLength);
            Task sending = conn.SendLoopAsync();
            try
            {
                await ReceiveLoopAsync(socket, conn);
            }
            catch (Exception e) when (e is OperationCanceledException || e is WebSocketException || e is System.IO.IOException)
            {
                // Idle timeout, client vanished, or we dropped it.
            }
            finally
            {
                conn.Room?.PostLeft(conn);
                // Let queued frames (like the reason we're closing) go out, but don't wait on a stalled client.
                conn.Close();
                if (await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(5))) != sending) conn.Abort();
                await sending;
                _connections.Release(ip);
            }
        }

        async Task ReceiveLoopAsync(WebSocket socket, Connection conn)
        {
            var buffer = new byte[_options.MaxMessageBytes];
            var frames = new RateLimiter(_options.FramesPerSecond, _options.FrameBurst, _clock);
            var idle = TimeSpan.FromSeconds(_options.IdleTimeoutSeconds);
            int strikes = 0;

            while (true)
            {
                int count = 0;
                ValueWebSocketReceiveResult result;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(conn.Aborted))
                {
                    timeout.CancelAfter(idle);
                    do
                    {
                        if (count == buffer.Length)
                        {
                            // Oversized: drop the client before buffering any more of it.
                            conn.Send(Protocol.ErrorFrame("Message too large."));
                            return;
                        }
                        result = await socket.ReceiveAsync(buffer.AsMemory(count), timeout.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        count += result.Count;
                    } while (!result.EndOfMessage);
                }

                bool ok = result.MessageType == WebSocketMessageType.Binary && count > 0 && frames.Allow(0);
                if (ok)
                {
                    byte type = buffer[0];
                    if (type == Protocol.Heartbeat) continue;
                    ok = Dispatch(conn, type, buffer.AsSpan(1, count - 1).ToArray());
                }
                if (!ok && ++strikes >= StrikesBeforeKick)
                {
                    conn.Send(Protocol.ErrorFrame("Too many invalid messages."));
                    return;
                }
            }
        }

        /// <summary>Returns false for a transport-level violation (counts as a strike).</summary>
        bool Dispatch(Connection conn, byte type, byte[] payload)
        {
            Room room = conn.Room;
            if (room != null)
            {
                if (type == Protocol.CreateRoom || type == Protocol.JoinRoom) return false;
                return room.PostFrame(conn, type, payload);
            }

            if (type != Protocol.CreateRoom && type != Protocol.JoinRoom)
            {
                conn.Send(Protocol.ErrorFrame("Create or join a room first."));
                return false;
            }
            if (conn.Joining) return true; // answer to the last attempt is on its way

            // Room codes are the main secret, so creating and joining share a slow per-IP budget.
            if (!_joinAttempts.Allow(conn.Ip))
            {
                conn.Send(Protocol.ErrorFrame("Too many attempts. Wait a minute and try again."));
                return true;
            }

            try
            {
                if (type == Protocol.CreateRoom)
                {
                    Protocol.CreateRequest req = Protocol.DecodeCreate(payload);
                    JoinRequest hostJoin = JoinRequest.Decode(req.Join);
                    if (hostJoin.Version != GameSession.ProtocolVersion)
                    {
                        conn.Send(Protocol.ErrorFrame("Game version mismatch."));
                        return true;
                    }
                    conn.Joining = true;
                    Room created = _rooms.TryCreate(conn, hostJoin, req.MaxPlayers, req.Bots, out string error);
                    if (created == null)
                    {
                        conn.Joining = false;
                        conn.Send(Protocol.ErrorFrame(error));
                    }
                    return true;
                }

                Protocol.DecodeJoin(payload, out string code, out byte[] join);
                Room target = _rooms.Find(code);
                if (target == null)
                {
                    conn.Send(Protocol.ErrorFrame("No room with that code."));
                    return true;
                }
                conn.Joining = true;
                if (!target.PostJoin(conn, join))
                {
                    conn.Joining = false;
                    conn.Send(Protocol.ErrorFrame("No room with that code."));
                }
                return true;
            }
            catch (WireException)
            {
                conn.Send(Protocol.ErrorFrame("Invalid request."));
                return false;
            }
        }
    }
}
