using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Conquer.Core.Net;
using Conquer.Server;

namespace Conquer.Client
{
    /// <summary>
    /// Local network play. The host runs the game server inside this window and joins it like any online room;
    /// the room is announced on the network so others see it on their Local network screen and join with a click.
    /// Everything after that (lobby, chat, reconnecting) is the online mode unchanged.
    /// </summary>
    public partial class MainWindow
    {
        LanServer _lanServer;
        IDisposable _lanBeacon, _lanListen;
        volatile LanGame _lanAdvert;
        IReadOnlyList<LanGame> _lanGames = Array.Empty<LanGame>();
        string _lanListenError;

        /// <summary>Which form connection errors go back to (Online, or Local network).</summary>
        Modal _netForm = Modal.Online;

        /// <summary>The server of the room we're in, for reconnecting to the same one.</summary>
        Uri _lastServer;

        /// <summary>The port this window hosts a LAN game on, or null (exposed for tests).</summary>
        public int? LanPort => _lanServer?.Port;

        /// <summary>Games heard on the network (exposed for tests).</summary>
        public IReadOnlyList<LanGame> LanGames => _lanGames;

        void OpenLan()
        {
            _netError = null;
            _netForm = Modal.Lan;
            OpenModal(Modal.Lan);
        }

        /// <summary>Listens for announced games only while the Local network screen is open.</summary>
        void SyncLanListening()
        {
            bool want = _modal == Modal.Lan;
            if (want && _lanListen == null)
            {
                try
                {
                    _lanListen = LanDiscovery.Listen(games => Dispatcher.UIThread.Post(() =>
                    {
                        _lanGames = games;
                        if (_modal == Modal.Lan) BuildOverlay();
                    }));
                    _lanListenError = null;
                }
                catch (SocketException)
                {
                    _lanListenError = "Couldn't listen for games on this network.";
                }
            }
            else if (!want && _lanListen != null)
            {
                _lanListen.Dispose();
                _lanListen = null;
                _lanGames = Array.Empty<LanGame>();
            }
        }

        // ---- The screen ----------------------------------------------------------------------------

        Control BuildLanForm()
        {
            var col = Ui.Column(10,
                Ui.Text("Local network", 26, true),
                Ui.Text("For people on the same Wi-Fi or network. No game server needed: one of you hosts.", 13, false, Ui.Muted),
                Field("Your name", _net.Name, v => _net.Name = v, "Name"),
                Field("Room password", _netPassword, v => _netPassword = v, "optional", secret: true));

            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Text("Host a game", 15, true));
            col.Children.Add(Ui.Stepper("Seats", _netMaxPlayers, 2, 6, v => _netMaxPlayers = v, 136));
            col.Children.Add(Ui.Button("Host game", HostLan, !_netBusy, primary: true, minWidth: 160));
            col.Children.Add(Ui.Text("Windows may ask whether to let Conquer use the network. Allow it on private networks so others can join.", 12, false, Ui.Muted));

            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Text("Games on your network", 15, true));
            if (_lanListenError != null) col.Children.Add(Ui.Text(_lanListenError, 13, false, Ui.Muted));
            else if (_lanGames.Count == 0) col.Children.Add(Ui.Text("Looking for games...", 13, false, Ui.Muted));
            foreach (LanGame g in _lanGames)
            {
                LanGame game = g;
                bool full = g.Players >= g.Seats;
                var name = Ui.Text($"{g.Host}'s game", 14, true);
                name.Width = 220;
                name.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                var seats = Ui.Text($"{g.Players} of {g.Seats} players", 13, false, Ui.Muted);
                seats.Width = 120;
                seats.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                col.Children.Add(Ui.Row(10, name, seats, Ui.Button(full ? "Full" : "Join", () => JoinLan(game), !full && !_netBusy, primary: true, minWidth: 80)));
            }
            col.Children.Add(Ui.Text("Not listed? Choose Online and type the host's address and room code; the host's lobby shows both.", 12, false, Ui.Muted));

            if (_netBusy) col.Children.Add(Ui.Text("Connecting...", 13, false, Ui.Muted));
            if (_netError != null) col.Children.Add(Ui.Text(_netError, 13, true, Color.FromRgb(0xff, 0x9a, 0x8c)));
            col.Children.Add(new Border { Height = 4 });
            col.Children.Add(Ui.Button("Back", () =>
            {
                CancelPending();
                StopLanHost();
                OpenModal(Modal.Start);
            }));
            return Ui.Card(col, 520);
        }

        /// <summary>The host's addresses, for people whose computer doesn't hear the announcement.</summary>
        Control LanAddressHint()
        {
            string where = string.Join(" or ", LanDiscovery.LocalAddresses().Select(a => $"{a}:{_lanServer.Port}"));
            if (where.Length == 0) where = $"this computer's address, port {_lanServer.Port}";
            var text = new SelectableTextBlock
            {
                Text = $"Not showing up for someone? They can choose Online, enter server {where} and the room code.",
                FontSize = 12,
                Foreground = Palette.Brush(Ui.Muted),
                TextWrapping = TextWrapping.Wrap,
            };
            return text;
        }

        // ---- Hosting and joining -------------------------------------------------------------------

        /// <summary>Starts the server in this window and creates a room on it (same path as the Host game button).</summary>
        public async void HostLan()
        {
            _netForm = Modal.Lan;
            if (_net.Name.Trim().Length == 0)
            {
                _netError = "Enter your name.";
                OpenModal(Modal.Lan);
                return;
            }

            StopLanHost();
            _netError = null;
            _netBusy = true;
            BuildOverlay();
            try
            {
                _lanServer = await LanServer.StartAsync();
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.IO.IOException || e is SocketException)
            {
                _netBusy = false;
                _netError = e is InvalidOperationException ? e.Message : "Couldn't start hosting: " + e.Message;
                OpenModal(Modal.Lan);
                return;
            }

            _c.Changed += UpdateLanAdvert;
            _lanBeacon = LanDiscovery.Announce(() => _lanAdvert);
            Connect(create: true, server: new Uri($"ws://127.0.0.1:{_lanServer.Port}/ws"));
        }

        /// <summary>Joins a game from the list (same path as its Join button; exposed for tests).</summary>
        public void JoinLan(LanGame game)
        {
            _netForm = Modal.Lan;
            Connect(create: false, server: new Uri($"ws://{game.Address}:{game.Port}/ws"), code: game.Code);
        }

        /// <summary>Keeps the announcement in step with the lobby; runs on the UI thread.</summary>
        void UpdateLanAdvert()
        {
            OnlineSession s = _c.Online;
            if (_lanServer == null || s == null || !s.IsHost || s.Status == OnlineStatus.Playing || string.IsNullOrEmpty(s.RoomCode))
            {
                _lanAdvert = null;
                return;
            }
            _lanAdvert = new LanGame
            {
                Port = _lanServer.Port,
                Code = s.RoomCode,
                Host = s.Name,
                Players = s.Players.Count,
                Seats = s.MaxPlayers,
            };
        }

        /// <summary>Stops announcing and shuts the hosted server down, which drops anyone still connected.</summary>
        void StopLanHost()
        {
            _c.Changed -= UpdateLanAdvert;
            _lanAdvert = null;
            _lanBeacon?.Dispose();
            _lanBeacon = null;
            LanServer server = _lanServer;
            _lanServer = null;
            if (server != null) _ = server.DisposeAsync().AsTask();
        }

        protected override void OnClosed(EventArgs e)
        {
            _c.LeaveOnline();
            StopLanHost();
            _lanListen?.Dispose();
            _lanListen = null;
            base.OnClosed(e);
        }
    }
}
