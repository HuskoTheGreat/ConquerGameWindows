using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Catan.Core;
using Catan.Core.Net;
using Catan.View;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Catan.Net
{
    /// <summary>
    /// Netcode for GameObjects + Unity Relay adapter. This class is deliberately thin: it moves bytes. Every
    /// security decision lives in the unit-tested <see cref="GameSession"/> (host side) and the codecs.
    ///
    /// Security model
    ///  - The host owns the only real <see cref="Game"/>. Clients send command bytes and get back a private,
    ///    read-only snapshot containing only what they may know.
    ///  - Who a command is "from" is the transport's sender id mapped to a seat, never a field in the message.
    ///  - Relay is used with DTLS, so traffic is encrypted in transit.
    ///  - Join requests are bounded, version-checked, optionally password protected (constant-time compare,
    ///    lockout on brute force) and seat tokens are required to reclaim a seat.
    ///  - Custom named messages are used instead of RPCs so every payload goes through our bounded decoders.
    ///
    /// Not covered: the host machine sees everything it hosts, so a modified host can cheat. That is inherent
    /// to peer-hosted games; a dedicated server would be the fix.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    [RequireComponent(typeof(HotSeatController))]
    public sealed class NetworkGameManager : MonoBehaviour, IGameLink
    {
        const string CmdMsg = "catan.cmd";
        const string SnapMsg = "catan.snap";
        const string WelcomeMsg = "catan.welcome";
        const string RosterMsg = "catan.roster";
        const string LogMsg = "catan.log";
        const string ErrMsg = "catan.err";
        const string ChatMsg = "catan.chat";

        public string playerName = "Player";
        public string joinCode = "";
        public string password = "";
        [Range(2, 6)] public int maxPlayers = 4;
        [Range(1, 6)] public int boardRadius = 2;

        NetworkManager _nm;
        UnityTransport _utp;
        GameSession _session; // host only

        Game _mirror;
        int _seat = -1;
        byte[] _token;        // memory only: never written to disk or logged
        string _status = "Not connected.";
        string _hostedCode = "";
        bool _busy;
        bool _joinedOrHosting;
        SessionState _state;
        List<string> _roster = new List<string>();

        public int Seat => _seat;
        public Game Mirror => _mirror;
        public event Action Updated;
        public event Action<string> LogReceived;
        public event Action<string> ErrorReceived;
        public event Action<int, string> ChatReceived;

        public IVoiceChat Voice { get; private set; }
        public bool IsHost => _session != null;
        readonly List<string> _lobbyChat = new List<string>();
        string _lobbyInput = "";

        ulong LocalId => _nm.LocalClientId;

        void Awake()
        {
            _nm = GetComponent<NetworkManager>();
            _utp = GetComponent<UnityTransport>();
            if (_nm.NetworkConfig == null) _nm.NetworkConfig = new NetworkConfig();
            _nm.NetworkConfig.NetworkTransport = _utp;
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.EnableSceneManagement = false;
            _nm.NetworkConfig.ClientConnectionBufferTimeout = 8; // drop half-open connections quickly
        }

        void Start()
        {
            Voice = GetComponent<IVoiceChat>(); // optional: present only if a voice component is on this object
            GetComponent<HotSeatController>().AttachLink(this);
        }

        void OnDestroy()
        {
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
        }

        // ---- Unity Gaming Services -----------------------------------------------------------------

        static async Task EnsureServices()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async void HostClicked() => await HostAsync();
        async void JoinClicked() => await JoinAsync();

        async Task HostAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _status = "Signing in...";
                await EnsureServices();

                _status = "Creating relay...";
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                _hostedCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                _utp.SetRelayServerData(new RelayServerData(allocation, "dtls"));

                // The session exists before the first client can possibly connect.
                _session = new GameSession(NetworkManager.ServerClientId, playerName, maxPlayers, password,
                    () => Time.realtimeSinceStartupAsDouble);
                _nm.ConnectionApprovalCallback = Approve;
                _nm.OnClientConnectedCallback += OnClientConnected;
                _nm.OnClientDisconnectCallback += OnClientDisconnected;

                if (!_nm.StartHost()) throw new InvalidOperationException("Could not start the host.");
                _nm.CustomMessagingManager.RegisterNamedMessageHandler(CmdMsg, OnCommandMessage);
                _nm.CustomMessagingManager.RegisterNamedMessageHandler(ChatMsg, OnChatMessage);
                Voice?.Prepare(_session.VoiceChannel, 0, playerName);

                _seat = 0;
                _token = _session.TokenFor(NetworkManager.ServerClientId);
                _joinedOrHosting = true;
                _state = SessionState.Lobby;
                RefreshRosterLocal();
                _status = "Hosting. Share the join code.";
            }
            catch (Exception e)
            {
                _status = "Host failed: " + e.Message;
                Disconnect();
            }
            finally
            {
                _busy = false;
            }
        }

        async Task JoinAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                string code = (joinCode ?? "").Trim().ToUpperInvariant();
                if (code.Length < 4 || code.Length > 12) throw new ArgumentException("Enter the join code.");

                _status = "Signing in...";
                await EnsureServices();

                _status = "Joining relay...";
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(code);
                _utp.SetRelayServerData(new RelayServerData(allocation, "dtls"));

                var request = new JoinRequest { Name = playerName, Password = password, Token = _token ?? new byte[0] };
                _nm.NetworkConfig.ConnectionData = request.Encode();
                _nm.OnClientDisconnectCallback += OnClientDisconnected;

                if (!_nm.StartClient()) throw new InvalidOperationException("Could not start the client.");
                var messaging = _nm.CustomMessagingManager;
                messaging.RegisterNamedMessageHandler(WelcomeMsg, OnWelcome);
                messaging.RegisterNamedMessageHandler(RosterMsg, OnRoster);
                messaging.RegisterNamedMessageHandler(SnapMsg, OnSnapshot);
                messaging.RegisterNamedMessageHandler(LogMsg, OnLog);
                messaging.RegisterNamedMessageHandler(ErrMsg, OnError);
                messaging.RegisterNamedMessageHandler(ChatMsg, OnChatBroadcast);

                _joinedOrHosting = true;
                _status = "Connecting...";
            }
            catch (Exception e)
            {
                _status = "Join failed: " + e.Message;
                Disconnect();
            }
            finally
            {
                _busy = false;
            }
        }

        void Disconnect()
        {
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            _session = null;
            _joinedOrHosting = false;
        }

        // ---- Host: approval and connection events --------------------------------------------------

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            // The payload is attacker-controlled: bound it before anything parses it.
            if (_session == null || request.Payload == null || request.Payload.Length > JoinRequest.MaxBytes)
            {
                response.Approved = false;
                response.Reason = "Rejected.";
                return;
            }

            JoinResult result = _session.Join(request.ClientNetworkId, request.Payload);
            response.Approved = result.Ok;
            response.Reason = result.Reason ?? "";
        }

        void OnClientConnected(ulong clientId)
        {
            if (!_nm.IsServer || clientId == LocalId) return;

            SendTo(clientId, WelcomeMsg, _session.WelcomeFor(clientId));
            byte[] snapshot = _session.SnapshotFor(clientId); // non-null only when reconnecting mid-game
            if (snapshot != null) SendTo(clientId, SnapMsg, snapshot);
            BroadcastRoster();
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_nm.IsServer)
            {
                if (clientId == LocalId) return;
                _session?.Disconnected(clientId);
                BroadcastRoster();
            }
            else
            {
                _status = "Disconnected: " + (string.IsNullOrEmpty(_nm.DisconnectReason) ? "connection closed." : _nm.DisconnectReason);
                _joinedOrHosting = false;
            }
        }

        // ---- Host: receiving commands --------------------------------------------------------------

        void OnCommandMessage(ulong sender, FastBufferReader reader)
        {
            if (_session == null) return;

            if (!TryRead(reader, CommandCodec.MaxBytes + 16, out byte[] bytes))
            {
                if (_session.Strike(sender)) _nm.DisconnectClient(sender, "Too many invalid messages.");
                return;
            }
            ProcessCommand(sender, bytes);
        }

        void ProcessCommand(ulong sender, byte[] bytes)
        {
            SessionResponse response = _session.HandleCommand(sender, bytes);

            if (response.Ok)
            {
                BroadcastState(response.Events);
            }
            else if (sender == LocalId)
            {
                ErrorReceived?.Invoke(response.Error);
            }
            else
            {
                SendTo(sender, ErrMsg, LogCodec.Encode(new[] { response.Error }));
            }

            if (response.Disconnect && sender != LocalId) _nm.DisconnectClient(sender, "Too many invalid messages.");
        }

        void BroadcastState(IReadOnlyList<string> events)
        {
            byte[] log = LogCodec.Encode(events);
            foreach (ulong id in _session.ConnectedClients)
            {
                if (id == LocalId)
                {
                    DeliverLocalState(events);
                    continue;
                }
                SendTo(id, SnapMsg, _session.SnapshotFor(id));
                SendTo(id, LogMsg, log);
            }
        }

        void DeliverLocalState(IReadOnlyList<string> events)
        {
            byte[] snapshot = _session.SnapshotFor(LocalId);
            if (snapshot != null) _mirror = SnapshotCodec.Decode(snapshot);
            foreach (string line in events) LogReceived?.Invoke(line);
            Updated?.Invoke();
        }

        void BroadcastRoster()
        {
            RefreshRosterLocal();
            byte[] roster = _session.RosterBytes();
            foreach (ulong id in _session.ConnectedClients)
            {
                if (id != LocalId) SendTo(id, RosterMsg, roster);
            }
        }

        void RefreshRosterLocal()
        {
            _roster = Welcome.DecodeRoster(_session.RosterBytes(), out _state);
        }

        void StartGameClicked()
        {
            string error = _session.Start(LocalId, new StartSettings { Radius = boardRadius, Rules = new HouseRules() });
            if (error != null)
            {
                _status = error;
                return;
            }
            BroadcastState(new[] { "The game has started." });
        }

        // ---- Client: receiving ---------------------------------------------------------------------

        void OnWelcome(ulong sender, FastBufferReader reader)
        {
            if (!TryRead(reader, 2048, out byte[] bytes)) return;
            try
            {
                Welcome w = Welcome.Decode(bytes);
                _seat = w.Seat;
                _token = w.Token;
                _roster = w.Names;
                _state = w.State;
                Voice?.Prepare(w.VoiceChannel, w.Seat, playerName);
                _status = "Connected. Waiting for the host to start.";
            }
            catch (WireException)
            {
                _status = "Invalid welcome from host.";
            }
        }

        void OnRoster(ulong sender, FastBufferReader reader)
        {
            if (!TryRead(reader, 2048, out byte[] bytes)) return;
            try
            {
                _roster = Welcome.DecodeRoster(bytes, out _state);
            }
            catch (WireException)
            {
            }
        }

        void OnSnapshot(ulong sender, FastBufferReader reader)
        {
            if (!TryRead(reader, SnapshotCodec.MaxBytes + 16, out byte[] bytes)) return;
            try
            {
                _mirror = SnapshotCodec.Decode(bytes);
                Updated?.Invoke();
            }
            catch (WireException)
            {
                _status = "Ignored an invalid game update.";
            }
        }

        void OnLog(ulong sender, FastBufferReader reader)
        {
            if (!TryRead(reader, LogCodec.MaxBytes + 16, out byte[] bytes)) return;
            try
            {
                foreach (string line in LogCodec.Decode(bytes)) LogReceived?.Invoke(line);
            }
            catch (WireException)
            {
            }
        }

        void OnError(ulong sender, FastBufferReader reader)
        {
            if (!TryRead(reader, LogCodec.MaxBytes + 16, out byte[] bytes)) return;
            try
            {
                foreach (string line in LogCodec.Decode(bytes)) ErrorReceived?.Invoke(line);
            }
            catch (WireException)
            {
            }
        }

        // ---- Client: sending -----------------------------------------------------------------------

        public void Submit(Command command)
        {
            byte[] bytes;
            try
            {
                bytes = CommandCodec.Encode(command);
            }
            catch (ArgumentException)
            {
                return;
            }

            if (_nm.IsServer) ProcessCommand(LocalId, bytes);
            else SendTo(NetworkManager.ServerClientId, CmdMsg, bytes);
        }

        // ---- Chat ----------------------------------------------------------------------------------

        public void SendChat(string text)
        {
            text = ChatCodec.Clean(text);
            if (text.Length == 0) return;
            byte[] bytes = ChatCodec.EncodeSend(text);

            if (_nm.IsServer) ProcessChat(LocalId, bytes);
            else SendTo(NetworkManager.ServerClientId, ChatMsg, bytes);
        }

        void OnChatMessage(ulong sender, FastBufferReader reader) // host: from a client
        {
            if (_session == null) return;
            if (!TryRead(reader, ChatCodec.MaxBytes + 16, out byte[] bytes))
            {
                if (_session.Strike(sender)) _nm.DisconnectClient(sender, "Too many invalid messages.");
                return;
            }
            ProcessChat(sender, bytes);
        }

        void ProcessChat(ulong sender, byte[] bytes)
        {
            ChatResult result = _session.HandleChat(sender, bytes);
            if (result.Ok)
            {
                byte[] broadcast = ChatCodec.EncodeBroadcast(result.Seat, result.Text);
                foreach (ulong id in _session.ConnectedClients)
                {
                    if (id == LocalId) DeliverChat(result.Seat, result.Text);
                    else SendTo(id, ChatMsg, broadcast);
                }
            }
            else if (sender == LocalId)
            {
                ErrorReceived?.Invoke(result.Error);
            }
            else
            {
                SendTo(sender, ErrMsg, LogCodec.Encode(new[] { result.Error }));
            }

            if (result.Disconnect && sender != LocalId) _nm.DisconnectClient(sender, "Too many invalid messages.");
        }

        void OnChatBroadcast(ulong sender, FastBufferReader reader) // client: from the host
        {
            if (!TryRead(reader, ChatCodec.MaxBytes + 16, out byte[] bytes)) return;
            try
            {
                ChatCodec.DecodeBroadcast(bytes, out int seat, out string text);
                DeliverChat(seat, text);
            }
            catch (WireException)
            {
            }
        }

        void DeliverChat(int seat, string text)
        {
            string who = seat >= 0 && seat < _roster.Count ? _roster[seat] : "?";
            _lobbyChat.Add($"{who}: {text}");
            if (_lobbyChat.Count > 8) _lobbyChat.RemoveAt(0);
            ChatReceived?.Invoke(seat, text);
        }

        public void SetChatMuted(int seat, bool muted)
        {
            string error = _session?.SetChatMuted(LocalId, seat, muted);
            if (error != null) ErrorReceived?.Invoke(error);
        }

        public bool IsChatMuted(int seat) => _session != null && _session.IsChatMuted(seat);

        // ---- Message plumbing ----------------------------------------------------------------------

        static bool TryRead(FastBufferReader reader, int maxBytes, out byte[] data)
        {
            data = null;
            if (reader.Length > maxBytes + 8) return false; // cheap size check before any allocation
            try
            {
                reader.ReadValueSafe(out data);
            }
            catch (Exception)
            {
                return false;
            }
            return data != null && data.Length <= maxBytes;
        }

        void SendTo(ulong target, string name, byte[] data)
        {
            if (data == null || _nm == null || !_nm.IsListening) return;
            int size = FastBufferWriter.GetWriteSize(data);
            using (var writer = new FastBufferWriter(size, Allocator.Temp))
            {
                writer.WriteValueSafe(data);
                _nm.CustomMessagingManager.SendNamedMessage(name, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        // ---- Lobby screen --------------------------------------------------------------------------

        GUIStyle _label, _button, _field, _title;
        float _styleScale = -1f;

        void OnGUI()
        {
            if (_mirror != null) return; // the game UI has taken over

            float u = Mathf.Max(1f, Screen.height / 720f);
            if (_label == null || !Mathf.Approximately(_styleScale, u))
            {
                _styleScale = u;
                _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * u), wordWrap = true };
                _title = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(20 * u), fontStyle = FontStyle.Bold };
                _button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15 * u) };
                _field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * u) };
            }

            var rect = new Rect((Screen.width - 460 * u) / 2f, (Screen.height - 520 * u) / 2f, 460 * u, 520 * u);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Catan: Online", _title);
            GUILayout.Label(_status, _label);

            GUI.enabled = !_busy && !_joinedOrHosting;
            GUILayout.Label("Your name", _label);
            playerName = GUILayout.TextField(playerName, 20, _field);
            GUILayout.Label("Room password (optional)", _label);
            password = GUILayout.PasswordField(password, '*', 32, _field);
            GUILayout.Space(8 * u);

            if (GUILayout.Button("Host a game", _button, GUILayout.Height(34 * u))) HostClicked();
            GUILayout.Space(6 * u);
            GUILayout.Label("Join code", _label);
            joinCode = GUILayout.TextField(joinCode, 12, _field);
            if (GUILayout.Button("Join game", _button, GUILayout.Height(34 * u))) JoinClicked();
            GUI.enabled = true;

            if (_joinedOrHosting)
            {
                GUILayout.Space(10 * u);
                if (_session != null) GUILayout.TextField(_hostedCode, _field); // read-only in spirit: copy and share
                GUILayout.Label($"Players ({_roster.Count}/{(_session != null ? maxPlayers : Math.Max(_roster.Count, 2))}):", _label);
                for (int i = 0; i < _roster.Count; i++) GUILayout.Label($"{i + 1}. {_roster[i]}" + (i == _seat ? " (you)" : ""), _label);

                if (_session != null)
                {
                    GUI.enabled = _roster.Count >= 2 && _state == SessionState.Lobby;
                    boardRadius = Mathf.RoundToInt(GUILayout.HorizontalSlider(boardRadius, 1, 6));
                    GUILayout.Label($"Board radius {boardRadius} ({Hex.CountForRadius(boardRadius)} tiles)", _label);
                    if (GUILayout.Button("Start game", _button, GUILayout.Height(34 * u))) StartGameClicked();
                    GUI.enabled = true;
                }
            }
            if (_joinedOrHosting)
            {
                GUILayout.Space(8 * u);
                foreach (string line in _lobbyChat) GUILayout.Label(line, _label);
                GUILayout.BeginHorizontal();
                _lobbyInput = GUILayout.TextField(_lobbyInput, ChatCodec.MaxChars, _field);
                if (GUILayout.Button("Send", _button, GUILayout.Width(70 * u)) && !string.IsNullOrWhiteSpace(_lobbyInput))
                {
                    SendChat(_lobbyInput);
                    _lobbyInput = "";
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
    }
}
