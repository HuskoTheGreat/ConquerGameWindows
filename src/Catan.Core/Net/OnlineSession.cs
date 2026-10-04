using System;
using System.Collections.Generic;

namespace Catan.Core.Net
{
    public enum OnlineStatus { Connecting, Lobby, Playing, Disconnected }

    /// <summary>
    /// The client's side of one online-server room, with no UI and no socket: frames go out through
    /// <see cref="IOnlineLink"/> and come back in through <see cref="Receive"/>. Everything from the server is
    /// decoded with the same bounded codecs the server uses, so a hostile server can't crash or inject markup.
    /// </summary>
    public sealed class OnlineSession
    {
        readonly IOnlineLink _link;
        string _lastFrameError;

        public OnlineSession(IOnlineLink link, string name, string password = "")
        {
            _link = link;
            Name = name;
            Password = password ?? "";
            _link.Received += Receive;
            _link.Closed += OnClosed;
        }

        public OnlineStatus Status { get; private set; } = OnlineStatus.Connecting;
        public string Name { get; }
        public string Password { get; }
        public string RoomCode { get; private set; }

        /// <summary>Our seat (0 is the host). -1 until the server welcomes us.</summary>
        public int Seat { get; private set; } = -1;
        public bool IsHost => Seat == 0;
        public int MaxPlayers { get; private set; }
        public IReadOnlyList<string> Players { get; private set; } = Array.Empty<string>();

        /// <summary>The 16-byte secret from the welcome; rejoining with it takes our seat back after a drop.</summary>
        public byte[] Token { get; private set; }

        /// <summary>The latest snapshot: a read-only game where only our own hand and cards are real.</summary>
        public Game Game { get; private set; }

        /// <summary>Why the connection ended, when it has.</summary>
        public string CloseReason { get; private set; }

        /// <summary>Lobby, roster or connection state changed.</summary>
        public event Action Changed;
        public event Action<Game> SnapshotReceived;
        public event Action<IReadOnlyList<string>> LogReceived;
        public event Action<string> ChatReceived;
        public event Action<string> ErrorReceived;

        // ---- Requests ------------------------------------------------------------------------------

        public void CreateRoom(int maxPlayers) => _link.Send(Protocol.EncodeCreate(maxPlayers, 0, JoinFor(null)));

        public void JoinRoom(string code)
        {
            RoomCode = (code ?? "").Trim().ToUpperInvariant();
            _link.Send(Protocol.EncodeJoin(RoomCode, JoinFor(null)));
        }

        /// <summary>Takes our seat back in <paramref name="code"/> with the token from an earlier welcome.</summary>
        public void Rejoin(string code, byte[] token)
        {
            RoomCode = code;
            _link.Send(Protocol.EncodeJoin(code, JoinFor(token)));
        }

        public void Start(int radius, HouseRules rules) => _link.Send(Protocol.EncodeStart(radius, rules));
        public void Send(Command command) => _link.Send(Protocol.Frame(Protocol.Command, CommandCodec.Encode(command)));

        public void Chat(string text)
        {
            text = ChatCodec.Clean(text);
            if (text.Length > 0) _link.Send(Protocol.Frame(Protocol.Chat, ChatCodec.EncodeSend(text)));
        }

        public void Leave() => _link.Close();

        JoinRequest JoinFor(byte[] token) => new JoinRequest { Name = Name, Password = Password, Token = token ?? new byte[0] };

        // ---- Frames from the server ----------------------------------------------------------------

        /// <summary>Handles one server frame. Anything malformed is dropped, never thrown.</summary>
        public void Receive(byte[] frame)
        {
            if (frame == null || frame.Length == 0 || Status == OnlineStatus.Disconnected) return;
            byte[] payload = new byte[frame.Length - 1];
            Buffer.BlockCopy(frame, 1, payload, 0, payload.Length);
            _lastFrameError = null;
            try
            {
                switch (frame[0])
                {
                    case Protocol.RoomCreated:
                        RoomCode = Protocol.DecodeRoomCreated(payload);
                        Changed?.Invoke();
                        break;
                    case Protocol.Welcome:
                        OnWelcome(Welcome.Decode(payload));
                        break;
                    case Protocol.Snapshot:
                        Game = SnapshotCodec.Decode(payload);
                        Status = OnlineStatus.Playing;
                        SnapshotReceived?.Invoke(Game);
                        break;
                    case Protocol.Log:
                        LogReceived?.Invoke(LogCodec.Decode(payload));
                        break;
                    case Protocol.ChatLine:
                        ChatCodec.DecodeBroadcast(payload, out int seat, out string text);
                        ChatReceived?.Invoke($"{(seat < Players.Count ? Players[seat] : "Player " + (seat + 1))}: {text}");
                        break;
                    case Protocol.BotChat:
                        Protocol.DecodeBotChat(payload, out string bot, out string line);
                        ChatReceived?.Invoke($"{bot}: {line}");
                        break;
                    case Protocol.Error:
                        string error = Protocol.DecodeError(payload);
                        // The server says why just before it closes a room or drops us.
                        _lastFrameError = error;
                        ErrorReceived?.Invoke(error);
                        break;
                }
            }
            catch (WireException)
            {
                // A frame we can't read; the next snapshot replaces the whole state anyway.
            }
        }

        void OnWelcome(Welcome w)
        {
            Seat = w.Seat;
            Token = w.Token;
            MaxPlayers = w.MaxPlayers;
            Players = w.Names;
            if (Status == OnlineStatus.Connecting || w.State == SessionState.Lobby)
                Status = w.State == SessionState.Playing ? OnlineStatus.Playing : OnlineStatus.Lobby;
            Changed?.Invoke();
        }

        void OnClosed(string reason)
        {
            if (Status == OnlineStatus.Disconnected) return;
            Status = OnlineStatus.Disconnected;
            CloseReason ??= _lastFrameError ?? reason;
            Changed?.Invoke();
        }
    }

    /// <summary>One connection to the server, carrying whole binary frames both ways.</summary>
    public interface IOnlineLink
    {
        void Send(byte[] frame);
        void Close();
        event Action<byte[]> Received;
        /// <summary>Raised once when the connection ends, with a reason a person can read.</summary>
        event Action<string> Closed;
    }
}
