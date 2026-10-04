using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Conquer.Core;
using Conquer.Core.Bots;
using Conquer.Core.Net;
using Conquer.Server.Bots;
using Microsoft.Extensions.Logging;

namespace Conquer.Server
{
    /// <summary>
    /// One game room. Every input (joins, frames, disconnects, bot lines, timer ticks) goes through a single
    /// queue processed by one loop, so the non-thread-safe <see cref="GameSession"/> and <see cref="Game"/> only
    /// ever see one action at a time and players acting at once can't corrupt state.
    /// </summary>
    public sealed class Room
    {
        abstract class Message { }
        sealed class CreateMsg : Message { public Connection Conn; }
        sealed class JoinMsg : Message { public Connection Conn; public byte[] Payload; }
        sealed class FrameMsg : Message { public Connection Conn; public byte Type; public byte[] Payload; }
        sealed class LeftMsg : Message { public Connection Conn; }
        sealed class BotMsg : Message { public BotPersona Persona; public string Text; }
        sealed class TickMsg : Message { }
        sealed class BotStepMsg : Message { }

        const int MaxQueuedFrames = 256;

        readonly Channel<Message> _inbox = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
        readonly Dictionary<ulong, Connection> _members = new Dictionary<ulong, Connection>();
        readonly ServerOptions _options;
        readonly Func<double> _clock;
        readonly BotDirector _bots;
        readonly BotCommentator _commentator;
        readonly ILogger _log;
        readonly double _created;
        int _queuedFrames;
        double _emptySince = double.NaN;
        double _finishedAt = double.NaN;
        bool _closed;
        readonly Dictionary<int, BotPlayer> _players = new Dictionary<int, BotPlayer>();
        bool _botStepQueued;

        public string Code { get; }
        public string CreatorIp { get; }
        internal GameSession Session { get; }
        public Task Completion { get; private set; }

        internal Room(string code, Connection host, JoinRequest hostJoin, int maxPlayers, int bots,
            ServerOptions options, Func<double> clock, BotDirector botDirector, ILogger log)
        {
            Code = code;
            CreatorIp = host.Ip;
            _options = options;
            _clock = clock;
            _bots = botDirector;
            _log = log;
            _created = clock();
            Session = new GameSession(host.Id, hostJoin.Name, maxPlayers, hostJoin.Password, clock);
            _commentator = new BotCommentator(options.Bots, clock);
            if (botDirector != null && botDirector.Enabled) _commentator.SetCount(bots);
            _inbox.Writer.TryWrite(new CreateMsg { Conn = host });
        }

        // ---- Posting (any thread) ------------------------------------------------------------------

        /// <summary>False if the room has already closed.</summary>
        public bool PostJoin(Connection conn, byte[] payload) => _inbox.Writer.TryWrite(new JoinMsg { Conn = conn, Payload = payload });
        public void PostLeft(Connection conn) => _inbox.Writer.TryWrite(new LeftMsg { Conn = conn });
        public void PostTick() => _inbox.Writer.TryWrite(new TickMsg());

        /// <summary>Returns false if the room is backed up; the frame is dropped (clients are rate limited anyway).</summary>
        public bool PostFrame(Connection conn, byte type, byte[] payload)
        {
            if (Interlocked.Increment(ref _queuedFrames) > MaxQueuedFrames)
            {
                Interlocked.Decrement(ref _queuedFrames);
                return false;
            }
            return _inbox.Writer.TryWrite(new FrameMsg { Conn = conn, Type = type, Payload = payload });
        }

        void PostBot(BotPersona persona, string text) => _inbox.Writer.TryWrite(new BotMsg { Persona = persona, Text = text });

        // ---- The loop ------------------------------------------------------------------------------

        public Task Start() => Completion = Task.Run(RunAsync);

        async Task RunAsync()
        {
            await foreach (Message m in _inbox.Reader.ReadAllAsync())
            {
                if (m is FrameMsg) Interlocked.Decrement(ref _queuedFrames);
                try
                {
                    Handle(m);
                }
                catch (Exception e)
                {
                    // A bug in one room must not take down the process. Log the type only, never payloads.
                    _log.LogError("Room loop error: {Type} {Message}", e.GetType().Name, e.Message);
                }
                if (_closed) break;
            }
            _inbox.Writer.TryComplete();

            // Anyone whose join was still queued gets an answer instead of waiting forever.
            while (_inbox.Reader.TryRead(out Message left))
            {
                if (left is JoinMsg j)
                {
                    j.Conn.Joining = false;
                    j.Conn.Send(Protocol.ErrorFrame("That room has closed."));
                }
            }
        }

        void Handle(Message m)
        {
            switch (m)
            {
                case CreateMsg c: OnCreate(c.Conn); break;
                case JoinMsg j: OnJoin(j.Conn, j.Payload); break;
                case FrameMsg f: OnFrame(f.Conn, f.Type, f.Payload); break;
                case LeftMsg l: OnLeft(l.Conn); break;
                case BotMsg b: OnBot(b.Persona, b.Text); break;
                case TickMsg _: OnTick(); break;
                case BotStepMsg _: OnBotStep(); break;
            }
        }

        void OnCreate(Connection host)
        {
            _members[host.Id] = host;
            host.Room = this;
            host.Joining = false;
            host.Send(Protocol.RoomCreatedFrame(Code));
            host.Send(Protocol.Frame(Protocol.Welcome, Session.WelcomeFor(host.Id)));
            host.Send(BotsFrame());
        }

        void OnJoin(Connection conn, byte[] payload)
        {
            conn.Joining = false;
            if (_closed || conn.Aborted.IsCancellationRequested) return;

            JoinResult result = Session.Join(conn.Id, payload, conn.Ip);
            if (!result.Ok)
            {
                conn.Send(Protocol.ErrorFrame(result.Reason));
                return;
            }

            _members[conn.Id] = conn;
            conn.Room = this;
            SendWelcomes();
            conn.Send(BotsFrame());
            if (Session.State == SessionState.Playing)
                conn.Send(Protocol.Frame(Protocol.Snapshot, Session.SnapshotFor(conn.Id)));
            Broadcast(Protocol.Frame(Protocol.Log, LogCodec.Encode(new[] { $"{Session.Seats[result.Seat].Name} joined." })));
        }

        void OnLeft(Connection conn)
        {
            if (!_members.Remove(conn.Id)) return;
            int seat = Session.SeatOf(conn.Id);
            string name = seat >= 0 ? Session.Seats[seat].Name : null;
            Session.Disconnected(conn.Id);
            SendWelcomes();
            if (name != null) Broadcast(Protocol.Frame(Protocol.Log, LogCodec.Encode(new[] { $"{name} left." })));
        }

        void OnFrame(Connection conn, byte type, byte[] payload)
        {
            if (!_members.ContainsKey(conn.Id)) return;

            switch (type)
            {
                case Protocol.Command:
                {
                    SessionResponse r = Session.HandleCommand(conn.Id, payload);
                    if (!r.Ok) Reject(conn, r.Error, r.Disconnect);
                    else StateChanged(r.Events);
                    break;
                }
                case Protocol.Chat:
                {
                    ChatResult r = Session.HandleChat(conn.Id, payload);
                    if (!r.Ok)
                    {
                        Reject(conn, r.Error, r.Disconnect);
                        break;
                    }
                    Broadcast(Protocol.Frame(Protocol.ChatLine, ChatCodec.EncodeBroadcast(r.Seat, r.Text)));
                    AskBot(_commentator.ObserveChat(Session.Seats[r.Seat].Name, r.Text));
                    break;
                }
                case Protocol.Start:
                {
                    StartSettings settings;
                    try
                    {
                        settings = Protocol.DecodeStart(payload);
                    }
                    catch (WireException)
                    {
                        Strike(conn);
                        break;
                    }
                    string error = settings.Radius > _options.MaxBoardRadius
                        ? $"This server allows boards up to {_options.MaxBoardRadius} rings."
                        : Session.Start(conn.Id, settings);
                    if (error != null)
                    {
                        conn.Send(Protocol.ErrorFrame(error));
                        break;
                    }
                    foreach (SeatInfo bot in Session.BotSeats) _players[bot.Seat] = new BotPlayer(bot.Seat, bot.Difficulty);
                    SendWelcomes();
                    StateChanged(new[] { "The game has started." });
                    break;
                }
                case Protocol.AddBot:
                case Protocol.RemoveBot:
                {
                    string error;
                    try
                    {
                        error = type == Protocol.AddBot
                            ? Session.AddBot(conn.Id, Protocol.DecodeAddBot(payload))
                            : Session.RemoveBot(conn.Id, Protocol.DecodeRemoveBot(payload));
                    }
                    catch (WireException)
                    {
                        Strike(conn);
                        break;
                    }
                    if (error != null) conn.Send(Protocol.ErrorFrame(error));
                    else SendWelcomes();
                    break;
                }
                case Protocol.Mute:
                {
                    try
                    {
                        Protocol.DecodeMute(payload, out int seat, out bool muted);
                        string error = Session.SetChatMuted(conn.Id, seat, muted);
                        if (error != null) conn.Send(Protocol.ErrorFrame(error));
                    }
                    catch (WireException)
                    {
                        Strike(conn);
                    }
                    break;
                }
                case Protocol.SetBots:
                {
                    int count;
                    try
                    {
                        count = Protocol.DecodeSetBots(payload);
                    }
                    catch (WireException)
                    {
                        Strike(conn);
                        break;
                    }
                    if (!Session.IsHost(conn.Id))
                    {
                        conn.Send(Protocol.ErrorFrame("Only the host can change the bots."));
                        break;
                    }
                    if (_bots == null || !_bots.Enabled)
                    {
                        conn.Send(Protocol.ErrorFrame("Bots aren't available on this server."));
                        break;
                    }
                    _commentator.SetCount(count);
                    Broadcast(BotsFrame());
                    break;
                }
                default:
                    Strike(conn);
                    break;
            }
        }

        void StateChanged(IReadOnlyList<string> events)
        {
            byte[] log = events.Count > 0 ? Protocol.Frame(Protocol.Log, LogCodec.Encode(events)) : null;
            foreach (Connection c in _members.Values)
            {
                byte[] snapshot = Session.SnapshotFor(c.Id);
                if (snapshot != null) c.Send(Protocol.Frame(Protocol.Snapshot, snapshot));
                if (log != null) c.Send(log);
            }
            if (Session.Game?.Phase == Phase.GameOver && double.IsNaN(_finishedAt)) _finishedAt = _clock();
            AskBot(_commentator.ObserveEvents(events));
            ScheduleBotStep();
        }

        // ---- Computer players ----------------------------------------------------------------------

        /// <summary>
        /// If a computer player might have something to do (its turn, a discard it owes, an offer to weigh),
        /// queue one step after a short pause. Steps run on this loop like any other input, one at a time.
        /// </summary>
        void ScheduleBotStep()
        {
            if (_botStepQueued || _closed || _players.Count == 0) return;
            Game g = Session.Game;
            if (g == null || g.Phase == Phase.GameOver) return;

            bool maybe = _players.ContainsKey(g.CurrentPlayer) || g.PendingTrade != null ||
                         g.PendingDiscards.Keys.Any(_players.ContainsKey);
            if (!maybe) return;

            _botStepQueued = true;
            Task.Delay(Math.Max(0, _options.BotMoveDelayMs)).ContinueWith(_ => _inbox.Writer.TryWrite(new BotStepMsg()), TaskScheduler.Default);
        }

        void OnBotStep()
        {
            _botStepQueued = false;
            if (_closed || Session.Game == null) return;

            foreach (BotPlayer bot in _players.Values)
            {
                Command command = bot.Decide(Session.Game);
                if (command == null) continue;

                SessionResponse r = Session.HandleBotCommand(bot.Seat, command);
                if (!r.Ok)
                {
                    // The bot misjudged; take a plain legal move instead so the game never stalls on it.
                    _log.LogWarning("Bot move refused: {Error}", r.Error);
                    Command fallback = bot.Fallback(Session.Game);
                    r = fallback != null ? Session.HandleBotCommand(bot.Seat, fallback) : r;
                }
                if (r.Ok) StateChanged(r.Events);
                return;
            }
        }

        void AskBot(BotPrompt prompt)
        {
            if (prompt == null) return;
            bool queued = _bots != null && _bots.TryEnqueue(new BotDirector.Job { Prompt = prompt, Done = PostBot });
            if (!queued) _commentator.Completed(prompt.Persona, null);
        }

        void OnBot(BotPersona persona, string text)
        {
            text = _commentator.Completed(persona, text);
            if (text == null || _closed || !_commentator.Active.Contains(persona)) return;
            Broadcast(Protocol.BotChatFrame(persona.Name, text));
        }

        void OnTick()
        {
            double now = _clock();
            bool anyone = Session.ConnectedClients.Any();
            if (anyone) _emptySince = double.NaN;
            else if (double.IsNaN(_emptySince)) _emptySince = now;

            double emptyLimit = 60.0 * (Session.State == SessionState.Lobby ? _options.EmptyLobbyMinutes : _options.EmptyGameMinutes);
            if (!double.IsNaN(_emptySince) && now - _emptySince > emptyLimit) Close("The room was empty for too long.");
            else if (!double.IsNaN(_finishedAt) && now - _finishedAt > 60.0 * _options.FinishedGameMinutes) Close("The game is over.");
            else if (now - _created > 3600.0 * _options.MaxRoomHours) Close("This room reached its time limit.");
        }

        /// <summary>Closes the room: tells everyone why and drops their connections. Used by the registry on shutdown too.</summary>
        internal void Close(string reason)
        {
            if (_closed) return;
            _closed = true;
            byte[] frame = Protocol.ErrorFrame(reason);
            foreach (Connection c in _members.Values)
            {
                c.Send(frame);
                c.Close();
            }
            _members.Clear();
        }

        void Reject(Connection conn, string error, bool disconnect)
        {
            conn.Send(Protocol.ErrorFrame(error));
            if (disconnect) conn.Close();
        }

        void Strike(Connection conn)
        {
            if (Session.Strike(conn.Id)) Reject(conn, "Too many invalid messages.", true);
        }

        void SendWelcomes()
        {
            // Lobby seats renumber when someone leaves, so everyone gets their current seat, not just a roster.
            foreach (Connection c in _members.Values)
            {
                byte[] welcome = Session.WelcomeFor(c.Id);
                if (welcome != null) c.Send(Protocol.Frame(Protocol.Welcome, welcome));
            }
        }

        byte[] BotsFrame() => Protocol.BotsFrame(_commentator.Active.Select(p => p.Name).ToList());

        void Broadcast(byte[] frame)
        {
            foreach (Connection c in _members.Values) c.Send(frame);
        }
    }
}
