using System;
using System.Collections.Generic;
using System.Linq;
using Conquer.Core.Bots;

namespace Conquer.Core.Net
{
    public enum SessionState { Lobby, Playing }

    /// <summary>What a joining client sends as its connection payload.</summary>
    public sealed class JoinRequest
    {
        public const int MaxBytes = 128;

        public byte Version = GameSession.ProtocolVersion;
        public string Name = "";
        /// <summary>Empty for a new player; the 16-byte token from an earlier Welcome to reclaim a seat.</summary>
        public byte[] Token = new byte[0];
        public string Password = "";

        public byte[] Encode()
        {
            var w = new WireWriter();
            w.Byte(Version);
            w.String(Name, 40);
            w.Raw(Token, GameSession.TokenBytes);
            w.String(Password, 32);
            return w.ToArray();
        }

        public static JoinRequest Decode(byte[] data)
        {
            var r = new WireReader(data, MaxBytes);
            var req = new JoinRequest
            {
                Version = (byte)r.Byte(),
                Name = r.String(40),
                Token = r.Raw(GameSession.TokenBytes),
                Password = r.String(32),
            };
            if (req.Token.Length != 0 && req.Token.Length != GameSession.TokenBytes) throw new WireException("Bad token.");
            r.End();
            return req;
        }
    }

    public sealed class JoinResult
    {
        public bool Ok { get; }
        public string Reason { get; }
        public int Seat { get; }

        JoinResult(bool ok, string reason, int seat)
        {
            Ok = ok;
            Reason = reason;
            Seat = seat;
        }

        internal static JoinResult Fail(string reason) => new JoinResult(false, reason, -1);
        internal static JoinResult Success(int seat) => new JoinResult(true, null, seat);
    }

    public sealed class SeatInfo
    {
        public int Seat { get; internal set; }
        public string Name { get; internal set; }
        internal byte[] Token;
        public ulong ClientId { get; internal set; }
        public bool Connected { get; internal set; }

        /// <summary>Set by the host: this player's chat messages are dropped.</summary>
        public bool ChatMuted { get; internal set; }

        /// <summary>A computer player run by the host. It has no connection and its seat can't be claimed.</summary>
        public bool IsBot { get; internal set; }
        public BotDifficulty Difficulty { get; internal set; }
    }

    public sealed class SessionResponse
    {
        public bool Ok { get; }
        public string Error { get; }

        /// <summary>The game state changed: send every connected client a fresh snapshot.</summary>
        public bool BroadcastState { get; }

        /// <summary>The client has misbehaved enough to be dropped.</summary>
        public bool Disconnect { get; }

        /// <summary>Public log lines describing what just happened (safe to show everyone).</summary>
        public IReadOnlyList<string> Events { get; }

        SessionResponse(bool ok, string error, bool broadcast, bool disconnect, IReadOnlyList<string> events)
        {
            Ok = ok;
            Error = error;
            BroadcastState = broadcast;
            Disconnect = disconnect;
            Events = events;
        }

        internal static SessionResponse Applied(IReadOnlyList<string> events) => new SessionResponse(true, null, true, false, events);
        internal static SessionResponse Rejected(string error, bool disconnect = false) =>
            new SessionResponse(false, error, false, disconnect, Array.Empty<string>());
    }

    public sealed class StartSettings
    {
        public int Radius = 2;
        public HouseRules Rules = new HouseRules();

        /// <summary>A board the host arranged before the game, or null for a random one of <see cref="Radius"/>.</summary>
        public Board Board;
    }

    /// <summary>
    /// Host-side authority for one multiplayer game: who sits where, who may join, and what a client's bytes
    /// are allowed to do. Transport-agnostic (the Netcode adapter just feeds it bytes), so every rule here is
    /// unit-tested without a network.
    /// </summary>
    public sealed class GameSession
    {
        public const byte ProtocolVersion = 4;
        public const int TokenBytes = 16;

        const int CommandsPerSecond = 8;
        const int CommandBurst = 16;
        const int ViolationsBeforeKick = 12;
        const double ChatPerSecond = 1.0;
        const int ChatBurst = 4;
        const int JoinFailuresBeforeLockout = 8;      // per source (IP address on the server)
        const int RoomFailuresBeforeLockout = 64;     // backstop across all sources
        const int MaxTrackedSources = 256;
        const double JoinFailureWindow = 60.0;
        const double LockoutSeconds = 30.0;

        readonly string _password;
        readonly int _maxPlayers;
        readonly Func<double> _clock;
        readonly List<SeatInfo> _seats = new List<SeatInfo>();
        readonly Dictionary<ulong, SeatInfo> _byClient = new Dictionary<ulong, SeatInfo>();
        readonly Dictionary<ulong, int> _violations = new Dictionary<ulong, int>();
        readonly RateLimiter _limiter;
        readonly RateLimiter _chatLimiter;

        sealed class FailureWindow
        {
            public int Count;
            public double Start;
            public double LockedUntil;
        }

        readonly Dictionary<string, FailureWindow> _failuresBySource = new Dictionary<string, FailureWindow>();
        readonly FailureWindow _roomFailures = new FailureWindow();

        public SessionState State { get; private set; } = SessionState.Lobby;
        public Game Game { get; private set; }
        public IReadOnlyList<SeatInfo> Seats => _seats;
        public int MaxPlayers => _maxPlayers;

        public GameSession(ulong hostClientId, string hostName, int maxPlayers, string password, Func<double> clock)
        {
            if (maxPlayers < 2 || maxPlayers > 6) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            _password = password ?? "";
            _maxPlayers = maxPlayers;
            _clock = clock;
            _limiter = new RateLimiter(CommandsPerSecond, CommandBurst, clock);
            _chatLimiter = new RateLimiter(ChatPerSecond, ChatBurst, clock);
            AddSeat(hostClientId, NameSanitizer.Clean(hostName, "Host"));
        }

        /// <summary>The host's own seat token (kept locally so the host can also resume after a crash).</summary>
        public byte[] TokenFor(ulong clientId) =>
            _byClient.TryGetValue(clientId, out SeatInfo s) ? (byte[])s.Token.Clone() : null;

        /// <summary>The host is whoever currently holds seat 0, so a reconnecting host keeps their powers.</summary>
        public bool IsHost(ulong clientId) => _byClient.TryGetValue(clientId, out SeatInfo s) && s.Seat == 0;

        public int SeatOf(ulong clientId) => _byClient.TryGetValue(clientId, out SeatInfo s) ? s.Seat : -1;

        public IEnumerable<ulong> ConnectedClients => _seats.Where(s => s.Connected && !s.IsBot).Select(s => s.ClientId);

        public IEnumerable<SeatInfo> BotSeats => _seats.Where(s => s.IsBot);

        static readonly string[] BotNames = { "Ada", "Turing", "Hopper", "Babbage", "Lovelace", "Knuth", "Dijkstra" };

        /// <summary>Host-only, in the lobby: seats a computer player. Returns an error message, or null on success.</summary>
        public string AddBot(ulong clientId, BotDifficulty difficulty)
        {
            if (!IsHost(clientId)) return "Only the host can add bots.";
            if (State != SessionState.Lobby) return "Bots can only be added before the game starts.";
            if (_seats.Count >= _maxPlayers) return "The lobby is full.";

            string name = BotNames.FirstOrDefault(n => _seats.All(s => !s.Name.StartsWith(n))) ?? "Bot";
            var seat = new SeatInfo
            {
                Seat = _seats.Count,
                Name = UniqueName($"{name} ({BotPlayer.Describe(difficulty)})"),
                Token = SecureRandom.Bytes(TokenBytes), // never sent anywhere; bot seats can't be reclaimed
                Connected = true,
                IsBot = true,
                Difficulty = difficulty,
            };
            _seats.Add(seat);
            return null;
        }

        /// <summary>Host-only, in the lobby. Returns an error message, or null on success.</summary>
        public string RemoveBot(ulong clientId, int seat)
        {
            if (!IsHost(clientId)) return "Only the host can remove bots.";
            if (State != SessionState.Lobby) return "Bots can only be removed before the game starts.";
            if (seat < 0 || seat >= _seats.Count || !_seats[seat].IsBot) return "That seat isn't a bot.";
            _seats.RemoveAt(seat);
            for (int i = 0; i < _seats.Count; i++) _seats[i].Seat = i;
            return null;
        }

        /// <summary>Applies a command chosen by the bot in <paramref name="seat"/> (the host runs the bots).</summary>
        public SessionResponse HandleBotCommand(int seat, Command command)
        {
            if (State != SessionState.Playing) return SessionResponse.Rejected("The game hasn't started.");
            if (seat < 0 || seat >= _seats.Count || !_seats[seat].IsBot || command.Player != seat)
                return SessionResponse.Rejected("Not a bot seat.");
            ActionResult result = Game.Apply(command);
            return result.Ok ? SessionResponse.Applied(result.Events) : SessionResponse.Rejected(result.Error);
        }

        // ---- Joining -------------------------------------------------------------------------------

        SeatInfo AddSeat(ulong clientId, string name)
        {
            var seat = new SeatInfo
            {
                Seat = _seats.Count,
                Name = UniqueName(name),
                Token = SecureRandom.Bytes(TokenBytes),
                ClientId = clientId,
                Connected = true,
            };
            _seats.Add(seat);
            _byClient[clientId] = seat;
            return seat;
        }

        string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; _seats.Any(s => s.Name == candidate); n++)
            {
                string suffix = $" ({n})";
                candidate = (name.Length + suffix.Length > NameSanitizer.MaxLength
                    ? name.Substring(0, NameSanitizer.MaxLength - suffix.Length)
                    : name) + suffix;
            }
            return candidate;
        }

        bool IsLockedOut(string source)
        {
            double now = _clock();
            if (now < _roomFailures.LockedUntil) return true;
            return _failuresBySource.TryGetValue(source, out FailureWindow w) && now < w.LockedUntil;
        }

        void RegisterJoinFailure(string source)
        {
            double now = _clock();
            if (!_failuresBySource.TryGetValue(source, out FailureWindow w))
            {
                if (_failuresBySource.Count >= MaxTrackedSources) PruneFailures(now);
                w = new FailureWindow { Start = now };
                _failuresBySource[source] = w;
            }
            Count(w, JoinFailuresBeforeLockout, now);
            Count(_roomFailures, RoomFailuresBeforeLockout, now);
        }

        static void Count(FailureWindow w, int limit, double now)
        {
            if (now - w.Start > JoinFailureWindow)
            {
                w.Start = now;
                w.Count = 0;
            }
            if (++w.Count >= limit) w.LockedUntil = now + LockoutSeconds;
        }

        void PruneFailures(double now)
        {
            foreach (string key in _failuresBySource
                         .Where(kv => now - kv.Value.Start > JoinFailureWindow && now >= kv.Value.LockedUntil)
                         .Select(kv => kv.Key).ToList())
                _failuresBySource.Remove(key);

            // Still full of live entries: forget the oldest rather than grow without bound.
            while (_failuresBySource.Count >= MaxTrackedSources)
                _failuresBySource.Remove(_failuresBySource.OrderBy(kv => kv.Value.Start).First().Key);
        }

        /// <summary>
        /// Connection-approval decision. <paramref name="payload"/> is attacker-controlled bytes.
        /// <paramref name="source"/> identifies where the attempt came from (the server passes the client's IP
        /// address); failed attempts lock out that source only, so one troll can't lock everyone out. A valid
        /// reconnect token always gets through, so a dropped player can rejoin even during a lockout.
        /// </summary>
        public JoinResult Join(ulong clientId, byte[] payload, string source = null)
        {
            source = source ?? "client:" + clientId;

            JoinRequest req;
            try
            {
                req = JoinRequest.Decode(payload);
            }
            catch (WireException)
            {
                bool locked = IsLockedOut(source);
                RegisterJoinFailure(source);
                return JoinResult.Fail(locked ? "Too many failed attempts. Try again shortly." : "Invalid join request.");
            }

            if (req.Version != ProtocolVersion) return JoinResult.Fail("Game version mismatch.");
            if (_byClient.ContainsKey(clientId)) return JoinResult.Fail("Already connected.");

            if (req.Token.Length > 0)
            {
                // The token is a 128-bit secret only handed out after a successful join (password included),
                // so it skips both the lockout and the password.
                SeatInfo mine = _seats.FirstOrDefault(s => !s.IsBot && ConstantTime.Equals(s.Token, req.Token));
                if (mine == null)
                {
                    bool locked = IsLockedOut(source);
                    RegisterJoinFailure(source);
                    return JoinResult.Fail(locked ? "Too many failed attempts. Try again shortly." : "Unknown session.");
                }
                if (mine.Connected) return JoinResult.Fail("That seat is already connected.");

                mine.ClientId = clientId;
                mine.Connected = true;
                _byClient[clientId] = mine;
                return JoinResult.Success(mine.Seat);
            }

            if (IsLockedOut(source)) return JoinResult.Fail("Too many failed attempts. Try again shortly.");

            if (_password.Length > 0 && !ConstantTime.Equals(req.Password, _password))
            {
                RegisterJoinFailure(source);
                return JoinResult.Fail("Wrong password.");
            }

            if (State != SessionState.Lobby) return JoinResult.Fail("The game has already started.");
            if (_seats.Count >= _maxPlayers) return JoinResult.Fail("The lobby is full.");

            string name = NameSanitizer.Clean(req.Name, "Player " + (_seats.Count + 1));
            return JoinResult.Success(AddSeat(clientId, name).Seat);
        }

        public void Disconnected(ulong clientId)
        {
            if (!_byClient.TryGetValue(clientId, out SeatInfo seat)) return;
            _byClient.Remove(clientId);
            _limiter.Forget(clientId);
            _chatLimiter.Forget(clientId);
            _violations.Remove(clientId);

            if (State == SessionState.Lobby && seat.Seat != 0)
            {
                _seats.Remove(seat); // lobby seats aren't reserved
                for (int i = 0; i < _seats.Count; i++) _seats[i].Seat = i;
            }
            else
            {
                seat.Connected = false; // in a game the seat is held for its token
            }
        }

        // ---- Starting ------------------------------------------------------------------------------

        /// <summary>Host-only. Returns an error message, or null on success.</summary>
        public string Start(ulong clientId, StartSettings settings)
        {
            if (!IsHost(clientId)) return "Only the host can start the game.";
            if (State != SessionState.Lobby) return "The game has already started.";
            if (_seats.Count < 2) return "At least two players are needed.";
            if (settings.Radius < BoardGenerator.MinRadius || settings.Radius > BoardGenerator.MaxRadius) return "Bad board size.";
            string rules = settings.Rules.Validate();
            if (rules != null) return rules;

            // Dice, deck and steals draw straight from a CSPRNG (a 32-bit seed could be brute-forced from the
            // rolls everyone sees). The board seed is public information anyway.
            if (settings.Board != null && settings.Board.Radius != settings.Radius) return "Bad board size.";
            var config = new GameConfig
            {
                PlayerCount = _seats.Count,
                PlayerNames = _seats.Select(s => s.Name).ToList(),
                Random = new SecureRng(),
                Board = new BoardConfig { Radius = settings.Radius, Seed = SecureRandom.NextInt() },
                Rules = settings.Rules,
            };
            Game = settings.Board != null ? new Game(settings.Board, config) : new Game(config);
            State = SessionState.Playing;
            return null;
        }

        // ---- Commands ------------------------------------------------------------------------------

        /// <summary>
        /// Handles one command's bytes from a client. The acting seat comes from the connection, never from
        /// the payload. Malformed or flooding clients accumulate violations and are eventually dropped.
        /// </summary>
        public SessionResponse HandleCommand(ulong clientId, byte[] payload)
        {
            if (!_byClient.TryGetValue(clientId, out SeatInfo seat)) return SessionResponse.Rejected("You are not in this game.");
            if (State != SessionState.Playing) return SessionResponse.Rejected("The game hasn't started.");

            if (!_limiter.Allow(clientId)) return Violation(clientId, "You're sending commands too fast.");

            Command command;
            try
            {
                command = CommandCodec.Decode(payload, seat.Seat);
            }
            catch (WireException)
            {
                return Violation(clientId, "Malformed command.");
            }

            ActionResult result = Game.Apply(command);
            return result.Ok ? SessionResponse.Applied(result.Events) : SessionResponse.Rejected(result.Error);
        }

        /// <summary>
        /// Records a transport-level violation (e.g. an oversized message dropped before it reached the
        /// codec). Returns true when the client should be disconnected.
        /// </summary>
        public bool Strike(ulong clientId) => Violation(clientId, "Invalid message.").Disconnect;

        SessionResponse Violation(ulong clientId, string message)
        {
            _violations.TryGetValue(clientId, out int count);
            _violations[clientId] = ++count;
            return SessionResponse.Rejected(message, disconnect: count >= ViolationsBeforeKick);
        }

        // ---- Chat ----------------------------------------------------------------------------------

        /// <summary>
        /// Handles one chat message. The sender's seat comes from the connection, so names can't be spoofed;
        /// text is cleaned and length-capped here, rate limited per client, and dropped for muted seats.
        /// </summary>
        public ChatResult HandleChat(ulong clientId, byte[] payload)
        {
            if (!_byClient.TryGetValue(clientId, out SeatInfo seat)) return ChatResult.Rejected("You are not in this game.");

            if (!_chatLimiter.Allow(clientId))
                return ChatResult.Rejected("You're chatting too fast.", Violation(clientId, "").Disconnect);

            string raw;
            try
            {
                raw = ChatCodec.DecodeSend(payload);
            }
            catch (WireException)
            {
                return ChatResult.Rejected("Malformed message.", Violation(clientId, "").Disconnect);
            }

            if (seat.ChatMuted) return ChatResult.Rejected("The host has muted you.");

            string text = ChatCodec.Clean(raw);
            if (text.Length == 0) return ChatResult.Rejected("Empty message.");
            return ChatResult.Accepted(seat.Seat, text);
        }

        /// <summary>Host-only chat moderation. Returns an error message, or null on success.</summary>
        public string SetChatMuted(ulong clientId, int seat, bool muted)
        {
            if (!IsHost(clientId)) return "Only the host can mute players.";
            if (seat < 0 || seat >= _seats.Count) return "No such player.";
            if (seat == 0) return "The host can't be muted.";
            _seats[seat].ChatMuted = muted;
            return null;
        }

        public bool IsChatMuted(int seat) => seat >= 0 && seat < _seats.Count && _seats[seat].ChatMuted;

        /// <summary>The events produced by the last applied command are not retained; clients diff snapshots or read the log from the host.</summary>
        public byte[] SnapshotFor(ulong clientId) =>
            State == SessionState.Playing && _byClient.TryGetValue(clientId, out SeatInfo s)
                ? SnapshotCodec.Encode(Game, s.Seat)
                : null;

        // ---- Welcome / lobby info ------------------------------------------------------------------

        /// <summary>Sent once, privately, after a client is approved: its seat, reconnect token and the roster.</summary>
        public byte[] WelcomeFor(ulong clientId)
        {
            if (!_byClient.TryGetValue(clientId, out SeatInfo s)) return null;
            var w = new WireWriter();
            w.Byte(ProtocolVersion);
            w.Byte(s.Seat);
            w.Raw(s.Token, TokenBytes);
            w.Byte(_maxPlayers);
            w.Byte((int)State);
            w.Byte(_seats.Count);
            foreach (SeatInfo seat in _seats) w.String(seat.Name, 80);
            return w.ToArray();
        }

        /// <summary>The roster everyone sees in the lobby (no tokens).</summary>
        public byte[] RosterBytes()
        {
            var w = new WireWriter();
            w.Byte((int)State);
            w.Byte(_seats.Count);
            foreach (SeatInfo seat in _seats) w.String(seat.Name, 80);
            return w.ToArray();
        }
    }

    public sealed class Welcome
    {
        public int Seat;
        public byte[] Token;
        public int MaxPlayers;
        public SessionState State;
        public List<string> Names = new List<string>();

        public static Welcome Decode(byte[] data)
        {
            var r = new WireReader(data, 1024);
            if (r.Byte() != GameSession.ProtocolVersion) throw new WireException("Version mismatch.");
            var w = new Welcome
            {
                Seat = r.Byte(5),
                Token = r.Raw(GameSession.TokenBytes),
                MaxPlayers = r.Byte(6),
                State = (SessionState)r.Byte(1),
            };
            if (w.Token.Length != GameSession.TokenBytes) throw new WireException("Bad token.");
            int n = r.Byte(6);
            for (int i = 0; i < n; i++) w.Names.Add(NameSanitizer.Clean(r.String(80), "Player " + (i + 1)));
            r.End();
            return w;
        }

        public static List<string> DecodeRoster(byte[] data, out SessionState state)
        {
            var r = new WireReader(data, 1024);
            state = (SessionState)r.Byte(1);
            int n = r.Byte(6);
            var names = new List<string>();
            for (int i = 0; i < n; i++) names.Add(NameSanitizer.Clean(r.String(80), "Player " + (i + 1)));
            r.End();
            return names;
        }
    }
}
