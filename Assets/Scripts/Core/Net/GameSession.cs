using System;
using System.Collections.Generic;
using System.Linq;

namespace Catan.Core.Net
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
    }

    /// <summary>
    /// Host-side authority for one multiplayer game: who sits where, who may join, and what a client's bytes
    /// are allowed to do. Transport-agnostic (the Netcode adapter just feeds it bytes), so every rule here is
    /// unit-tested without a network.
    /// </summary>
    public sealed class GameSession
    {
        public const byte ProtocolVersion = 1;
        public const int TokenBytes = 16;

        const int CommandsPerSecond = 8;
        const int CommandBurst = 16;
        const int ViolationsBeforeKick = 12;
        const int JoinFailuresBeforeLockout = 8;
        const double JoinFailureWindow = 60.0;
        const double LockoutSeconds = 30.0;

        readonly ulong _hostClient;
        readonly string _password;
        readonly int _maxPlayers;
        readonly Func<double> _clock;
        readonly List<SeatInfo> _seats = new List<SeatInfo>();
        readonly Dictionary<ulong, SeatInfo> _byClient = new Dictionary<ulong, SeatInfo>();
        readonly Dictionary<ulong, int> _violations = new Dictionary<ulong, int>();
        readonly RateLimiter _limiter;

        int _joinFailures;
        double _failWindowStart;
        double _lockedUntil;

        public SessionState State { get; private set; } = SessionState.Lobby;
        public Game Game { get; private set; }
        public IReadOnlyList<SeatInfo> Seats => _seats;
        public int MaxPlayers => _maxPlayers;

        public GameSession(ulong hostClientId, string hostName, int maxPlayers, string password, Func<double> clock)
        {
            if (maxPlayers < 2 || maxPlayers > 6) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            _hostClient = hostClientId;
            _password = password ?? "";
            _maxPlayers = maxPlayers;
            _clock = clock;
            _limiter = new RateLimiter(CommandsPerSecond, CommandBurst, clock);
            AddSeat(hostClientId, NameSanitizer.Clean(hostName, "Host"));
        }

        /// <summary>The host's own seat token (kept locally so the host can also resume after a crash).</summary>
        public byte[] TokenFor(ulong clientId) =>
            _byClient.TryGetValue(clientId, out SeatInfo s) ? (byte[])s.Token.Clone() : null;

        public int SeatOf(ulong clientId) => _byClient.TryGetValue(clientId, out SeatInfo s) ? s.Seat : -1;

        public IEnumerable<ulong> ConnectedClients => _seats.Where(s => s.Connected).Select(s => s.ClientId);

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

        void RegisterJoinFailure()
        {
            double now = _clock();
            if (now - _failWindowStart > JoinFailureWindow)
            {
                _failWindowStart = now;
                _joinFailures = 0;
            }
            if (++_joinFailures >= JoinFailuresBeforeLockout) _lockedUntil = now + LockoutSeconds;
        }

        /// <summary>Connection-approval decision. <paramref name="payload"/> is attacker-controlled bytes.</summary>
        public JoinResult Join(ulong clientId, byte[] payload)
        {
            if (_clock() < _lockedUntil) return JoinResult.Fail("Too many failed attempts. Try again shortly.");

            JoinRequest req;
            try
            {
                req = JoinRequest.Decode(payload);
            }
            catch (WireException)
            {
                RegisterJoinFailure();
                return JoinResult.Fail("Invalid join request.");
            }

            if (req.Version != ProtocolVersion) return JoinResult.Fail("Game version mismatch.");
            if (_byClient.ContainsKey(clientId)) return JoinResult.Fail("Already connected.");

            if (_password.Length > 0 && !ConstantTime.Equals(req.Password, _password))
            {
                RegisterJoinFailure();
                return JoinResult.Fail("Wrong password.");
            }

            if (req.Token.Length > 0)
            {
                SeatInfo mine = _seats.FirstOrDefault(s => ConstantTime.Equals(s.Token, req.Token));
                if (mine == null)
                {
                    RegisterJoinFailure();
                    return JoinResult.Fail("Unknown session.");
                }
                if (mine.Connected) return JoinResult.Fail("That seat is already connected.");

                mine.ClientId = clientId;
                mine.Connected = true;
                _byClient[clientId] = mine;
                return JoinResult.Success(mine.Seat);
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
            _violations.Remove(clientId);

            if (State == SessionState.Lobby && clientId != _hostClient)
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
            if (clientId != _hostClient) return "Only the host can start the game.";
            if (State != SessionState.Lobby) return "The game has already started.";
            if (_seats.Count < 2) return "At least two players are needed.";
            if (settings.Radius < BoardGenerator.MinRadius || settings.Radius > BoardGenerator.MaxRadius) return "Bad board size.";
            string rules = settings.Rules.Validate();
            if (rules != null) return rules;

            // Seeds come from a CSPRNG on the host and are never sent to clients.
            Game = new Game(new GameConfig
            {
                PlayerCount = _seats.Count,
                PlayerNames = _seats.Select(s => s.Name).ToList(),
                Seed = SecureRandom.NextInt(),
                Board = new BoardConfig { Radius = settings.Radius, Seed = SecureRandom.NextInt() },
                Rules = settings.Rules,
            });
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
