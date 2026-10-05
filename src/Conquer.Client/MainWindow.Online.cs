using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Conquer.Core.Net;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>The connect form, the room lobby and chat.</summary>
    public partial class MainWindow
    {
        static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        OnlineSettings _net = OnlineSettings.Load();
        string _netCode = "", _netPassword = "", _netError;
        int _netMaxPlayers = 4;
        bool _netBusy;
        OnlineSession _pending;
        bool _offlineOnly;

        readonly TextBox _chatBox = new TextBox { Watermark = "Say something...", MaxLength = Core.Net.ChatCodec.MaxChars };

        /// <summary>
        /// Opens straight to the single-player setup with no way to go online (for the local launcher, which
        /// never talks to a server).
        /// </summary>
        public bool OfflineOnly
        {
            get => _offlineOnly;
            set
            {
                _offlineOnly = value;
                if (value && _modal == Modal.Start) _modal = Modal.Setup;
                Rebuild();
            }
        }

        // ---- Connect form --------------------------------------------------------------------------

        Control BuildOnlineForm()
        {
            var col = Ui.Column(10,
                Ui.Heading("Play online"),
                Field("Server", _net.Server, v => _net.Server = v, "your-server.example.com"),
                Field("Your name", _net.Name, v => _net.Name = v, "Name"),
                Field("Room password", _netPassword, v => _netPassword = v, "optional", secret: true));

            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Section("New room"));
            col.Children.Add(Ui.Stepper("Seats", _netMaxPlayers, 2, 6, v => _netMaxPlayers = v, 136));
            col.Children.Add(Ui.Button("Create room", () => Connect(create: true), !_netBusy, primary: true, minWidth: 160));

            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Section("Join a friend's room"));
            col.Children.Add(Field("Room code", _netCode, v => _netCode = v, "from the host"));
            col.Children.Add(Ui.Button("Join room", () => Connect(create: false), !_netBusy, primary: true, minWidth: 160));

            if (_netBusy) col.Children.Add(Ui.Text("Connecting...", 13, false, Ui.Muted));
            if (_netError != null) col.Children.Add(Ui.Text(_netError, 13, true, Color.FromRgb(0xff, 0x9a, 0x8c)));
            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Button("Back", () =>
            {
                CancelPending();
                OpenModal(Modal.Start);
            }));
            return Ui.Card(col, 520);
        }

        static Control Field(string label, string value, Action<string> set, string watermark, bool secret = false)
        {
            var name = Ui.Text(label, 14);
            name.Width = 130;
            name.VerticalAlignment = VerticalAlignment.Center;
            var box = new TextBox { Text = value ?? "", Watermark = watermark, Width = 300, Name = label };
            if (secret) box.PasswordChar = '•';
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) set(box.Text ?? "");
            };
            return Ui.Row(10, name, box);
        }

        /// <summary>
        /// Creates, joins or rejoins a room. <paramref name="server"/> overrides the typed server (LAN games), and a
        /// rejoin goes back to whichever server the room was on.
        /// </summary>
        async void Connect(bool create, OnlineSession rejoin = null, Uri server = null, string code = null)
        {
            string name = rejoin?.Name ?? _net.Name.Trim();
            Uri address = server ?? (rejoin != null ? _lastServer : null) ?? WebSocketLink.ParseAddress(_net.Server);
            code ??= _netCode;
            _netError = address == null ? "Enter the server's address."
                : name.Length == 0 ? "Enter your name."
                : !create && rejoin == null && code.Trim().Length == 0 ? "Enter the room code."
                : null;
            if (_netError != null)
            {
                OpenModal(_netForm);
                return;
            }

            _net.Save();
            _lastServer = address;
            _netBusy = true;
            BuildOverlay();
            CancelPending();

            WebSocketLink link;
            try
            {
                link = await WebSocketLink.ConnectAsync(address, a => Dispatcher.UIThread.Post(a), ConnectTimeout);
            }
            catch (InvalidOperationException e)
            {
                _netBusy = false;
                _netError = e.Message;
                if (_c.IsOnline) BuildOverlay();
                else
                {
                    OpenModal(_netForm);
                    StopLanHost();
                }
                return;
            }

            var session = new OnlineSession(link, name, rejoin?.Password ?? _netPassword);
            _pending = session;
            session.ErrorReceived += message => { if (_pending == session && session.Seat < 0) FailPending(message); };
            session.Changed += () =>
            {
                if (_pending != session) return;
                if (session.Status == OnlineStatus.Disconnected) FailPending(session.CloseReason);
                else if (session.Seat >= 0) PlayOnline(session);
            };

            if (rejoin != null) session.Rejoin(rejoin.RoomCode, rejoin.Token);
            else if (create) session.CreateRoom(_netMaxPlayers);
            else session.JoinRoom(code);
        }

        /// <summary>Shows a room we've just been welcomed into (exposed for tests).</summary>
        public void PlayOnline(OnlineSession session)
        {
            _pending = null;
            _netBusy = false;
            _netError = null;
            _modal = Modal.None;
            _c.GoOnline(session);
        }

        void FailPending(string message)
        {
            OnlineSession s = _pending;
            _pending = null;
            s?.Leave();
            _netBusy = false;
            _netError = message ?? "Couldn't join the room.";
            // A failed reconnect stays on the disconnected screen, which shows the error.
            if (_c.IsOnline) BuildOverlay();
            else OpenModal(_netForm);
            if (!_c.IsOnline) StopLanHost();
        }

        void CancelPending()
        {
            _pending?.Leave();
            _pending = null;
            _netBusy = false;
        }

        void LeaveOnline()
        {
            _c.LeaveOnline();
            StopLanHost();
            _modal = _offlineOnly ? Modal.Setup : Modal.Start;
            Rebuild();
        }

        // ---- Lobby ---------------------------------------------------------------------------------

        Control BuildLobby()
        {
            OnlineSession s = _c.Online;
            var code = new SelectableTextBlock
            {
                Text = s.RoomCode ?? "",
                FontSize = 30,
                FontWeight = FontWeight.Bold,
                Foreground = Palette.Brush(Palette.Highlight),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var col = Ui.Column(10,
                Ui.Row(12, Ui.Text("Room", 26, true), code,
                    Ui.Button("Copy", () => TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(s.RoomCode ?? ""))),
                Ui.Text(_lanServer != null
                    ? "Players on your network will see this game under Local network."
                    : "Share the room code (and password, if you set one) with your friends.", 13, false, Ui.Muted),
                Ui.Text($"Players {s.Players.Count} of {s.MaxPlayers}", 15, true));

            if (_lanServer != null) col.Children.Add(LanAddressHint());

            for (int seat = 0; seat < s.Players.Count; seat++)
            {
                string tags = (seat == s.Seat ? "  (you)" : "") + (seat == 0 ? "  host" : "");
                var row = Ui.Row(8, Ui.Dot(Palette.Player(seat), 14), Ui.Text(s.Players[seat], 14, seat == s.Seat), Ui.Text(tags, 12, false, Ui.Muted));
                col.Children.Add(row);
            }

            if (s.IsHost)
            {
                // Computer players are single-player only for now, so an online game needs a second person.
                if (s.Players.Count < 2) col.Children.Add(Ui.Text("Waiting for at least one more player to join.", 13, false, Ui.Muted));
                col.Children.Add(new Border { Height = 4 });
                col.Children.Add(Ui.Stepper("Board radius", _setupRadius, BoardGenerator.MinRadius, 6, v =>
                {
                    _setupRadius = v;
                    if (_customBoard != null && _customBoard.Radius != v) BuildOverlay();
                }));
                col.Children.Add(BoardChoiceRow());
                col.Children.Add(Ui.Stepper("Points to win", _setupVp, 3, 20, v => _setupVp = v));
                col.Children.Add(Ui.Text("More options are under House Rules once the game starts.", 12, false, Ui.Muted));
                col.Children.Add(Ui.Row(8,
                    Ui.Button("Start game", () => s.Start(_setupRadius, new HouseRules { VictoryPoints = _setupVp }, _customBoard), s.Players.Count >= 2, primary: true, minWidth: 140),
                    Ui.Button("Leave room", LeaveOnline)));
            }
            else
            {
                col.Children.Add(Ui.Text(s.Status == OnlineStatus.Playing ? "Starting..." : "Waiting for the host to start the game.", 14, false, Ui.Muted));
                col.Children.Add(Ui.Button("Leave room", LeaveOnline));
            }
            return Ui.Card(col, 540);
        }

        Control BuildDisconnected()
        {
            OnlineSession s = _c.Online;
            bool canRejoin = s.Token != null && !string.IsNullOrEmpty(s.RoomCode);
            var col = Ui.Column(12,
                Ui.Heading("Disconnected"),
                Ui.Text(s.CloseReason ?? "The connection to the server was lost.", 14, false, Ui.Muted));
            if (_netBusy) col.Children.Add(Ui.Text("Reconnecting...", 13, false, Ui.Muted));
            if (_netError != null) col.Children.Add(Ui.Text(_netError, 13, true, Color.FromRgb(0xff, 0x9a, 0x8c)));
            var buttons = Ui.Row(8);
            if (canRejoin) buttons.Children.Add(Ui.Button("Reconnect", () => Connect(false, s), !_netBusy, primary: true, minWidth: 140));
            buttons.Children.Add(Ui.Button("Back to menu", LeaveOnline));
            col.Children.Add(buttons);
            return Ui.Card(col, 480);
        }

        // ---- Chat ----------------------------------------------------------------------------------

        Control BuildChatRow()
        {
            _chatBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                SendChat();
                e.Handled = true;
            };
            var send = Ui.Button("Send", SendChat);
            var row = new DockPanel { Margin = new Thickness(12, 0, 12, 12), Name = "Chat" };
            DockPanel.SetDock(send, Dock.Right);
            send.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(send);
            row.Children.Add(_chatBox);
            return row;
        }

        void SendChat()
        {
            if (_c.Online == null) return;
            _c.Online.Chat(_chatBox.Text);
            _chatBox.Text = "";
        }
    }

    /// <summary>The last server and name typed, so they're filled in next time. Missing or broken files are ignored.</summary>
    public sealed class OnlineSettings
    {
        public string Server { get; set; } = "";
        public string Name { get; set; } = Environment.UserName;

        /// <summary>Where settings live; tests point this somewhere harmless.</summary>
        public static string FilePath { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Conquer", "online.json");

        public static OnlineSettings Load()
        {
            try
            {
                if (FilePath != null && File.Exists(FilePath))
                    return JsonSerializer.Deserialize<OnlineSettings>(File.ReadAllText(FilePath)) ?? new OnlineSettings();
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
            }
            return new OnlineSettings();
        }

        public void Save()
        {
            try
            {
                if (FilePath == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
