using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Conquer.Client.Animation;
using Conquer.Core;

namespace Conquer.Client
{
    public sealed partial class MainWindow : Window
    {
        enum Modal { Start, Online, Setup, None, BankTrade, PlayerTrade, PlayCard, PickHarvest, PickPlunder, Rules }

        readonly LocalGameController _c = new LocalGameController();
        readonly BoardControl _board = new BoardControl();
        readonly StackPanel _status = new StackPanel { Spacing = 6 };
        readonly StackPanel _log = new StackPanel { Spacing = 3 };
        readonly ScrollViewer _logScroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        readonly StackPanel _bottom = new StackPanel { Spacing = 8 };
        readonly Grid _overlay = new Grid { IsVisible = false, Name = "Overlay" };
        readonly TextBlock _toast = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Brush(Color.FromRgb(0xff, 0x9a, 0x8c)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
        };
        readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };

        // Animation: a layer over the window, the card rows cards fly to, and the last view we diffed against.
        readonly AnimationLayer _anim = new AnimationLayer { Name = "Animations" };
        readonly CardRow _hand = new CardRow();
        readonly CardRow _bankRow = new CardRow();
        readonly Dictionary<int, Control> _seatAnchors = new Dictionary<int, Control>();
        GameView _view;
        int _viewGame = -1;
        readonly List<VisualEvent> _afterHandoff = new List<VisualEvent>();

        Modal _modal = Modal.Start;

        // Dialog working state
        int _setupPlayers = 3, _setupRadius = 2, _setupVp = 10;
        bool _setupHide = true;
        Resource _bankGive = Resource.Wasteland, _bankGet = Resource.Wasteland;
        readonly int[] _offerGive = new int[5], _offerWant = new int[5], _discardSel = new int[5];
        int _discardFor = -1;
        Resource _harvestFirst = Resource.Wasteland;
        HouseRules _draft;

        public MainWindow()
        {
            Title = "Conquer";
            Width = 1360;
            Height = 860;
            MinWidth = 1080;
            MinHeight = 700;
            Background = Palette.Brush(Color.FromRgb(0x14, 0x17, 0x1c));

            _board.Controller = _c;
            _c.Changed += ObserveGame;
            _c.Changed += () => Dispatcher.UIThread.Post(Rebuild);
            SetUpAnimations();
            _c.ToastShown += ShowToast;
            _toastTimer.Tick += (_, _) =>
            {
                _toast.Text = "";
                _toastTimer.Stop();
            };

            Content = BuildShell();
            Rebuild();
        }

        /// <summary>The game session behind this window (exposed for tests).</summary>
        public LocalGameController Controller => _c;

        /// <summary>Starts a game and closes the setup dialog (same path as the Start button).</summary>
        public void StartNewGame(int players, int radius, int victoryPoints, bool hideHands, int? seed = null, IDice dice = null)
        {
            _modal = Modal.None;
            _c.NewGame(players, radius, victoryPoints, hideHands, seed, dice);
            EnsureBotTimer();
        }

        Control BuildShell()
        {
            var main = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("340,*,320"),
                RowDefinitions = new RowDefinitions("*,Auto"),
            };

            var statusScroll = new ScrollViewer { Content = new Border { Padding = new Thickness(12), Child = _status } };
            Grid.SetColumn(statusScroll, 0);

            var boardHost = new Border { Margin = new Thickness(0), Child = _board, ClipToBounds = true };
            Grid.SetColumn(boardHost, 1);

            _logScroll.Content = new Border { Padding = new Thickness(12), Child = _log };
            var logColumn = new DockPanel();
            Control chat = BuildChatRow();
            DockPanel.SetDock(chat, Dock.Bottom);
            logColumn.Children.Add(chat);
            logColumn.Children.Add(_logScroll);
            Grid.SetColumn(logColumn, 2);

            var bottomBar = new Border
            {
                Background = Palette.Brush(Palette.Panel),
                BorderBrush = Palette.Brush(Palette.PanelEdge),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 10),
                Child = _bottom,
            };
            Grid.SetRow(bottomBar, 1);
            Grid.SetColumnSpan(bottomBar, 3);

            main.Children.Add(statusScroll);
            main.Children.Add(boardHost);
            main.Children.Add(logColumn);
            main.Children.Add(bottomBar);

            var root = new Grid();
            root.Children.Add(main);
            root.Children.Add(_anim);
            root.Children.Add(_overlay);
            root.Children.Add(_toast); // above dialogs, so lobby and connect errors show too
            return root;
        }

        // ---- Rebuilding the screen from state ------------------------------------------------------

        void Rebuild()
        {
            BuildStatus();
            BuildLog();
            BuildBottom();
            BuildOverlay();
            _board.InvalidateVisual();
        }

        void ShowToast(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _toast.Text = message;
                _toastTimer.Stop();
                _toastTimer.Start();
            });
        }

        void CloseModal()
        {
            _modal = Modal.None;
            BuildOverlay();
            BuildBottom();
        }

        void OpenModal(Modal modal)
        {
            _modal = modal;
            BuildOverlay();
            BuildBottom();
        }

        // ---- Animations ----------------------------------------------------------------------------

        /// <summary>The window's animation layer (exposed for tests).</summary>
        public AnimationLayer Animations => _anim;

        void SetUpAnimations()
        {
            _board.Effects = _anim.Board;
            _anim.Followers.Add(_board);
            _anim.Followers.Add(_hand);
            _anim.Followers.Add(_bankRow);
            _anim.NameOf = id => _c.Game?.Players[id].Name ?? $"Player {id + 1}";
            _anim.BoardArea = () => _board.TranslatePoint(new Point(0, 0), _anim) is Point o ? new Rect(o, _board.Bounds.Size) : new Rect(_anim.Bounds.Size);
            _anim.Resolve = ResolvePlace;
            _anim.Landed += (place, r) =>
            {
                if (place.Kind == PlaceKind.Player && place.Player == _c.Actor) _hand.Bump(r);
                else if (place.Kind == PlaceKind.Bank) _bankRow.Bump(r);
            };
        }

        Point? ResolvePlace(Place place, Resource? r)
        {
            switch (place.Kind)
            {
                case PlaceKind.Bank: return _bankRow.TranslatePoint(_bankRow.SlotCenter(r), _anim);
                case PlaceKind.DevDeck: return _bankRow.TranslatePoint(_bankRow.SlotCenter(null), _anim);
                case PlaceKind.Tile:
                    Point? t = _board.TileCenter(place.Hex);
                    return t.HasValue ? _board.TranslatePoint(t.Value, _anim) : null;
                case PlaceKind.Player:
                    if (_c.Game != null && place.Player == _c.Actor && _hand.IsVisible && _hand.Bounds.Width > 0)
                        return _hand.TranslatePoint(_hand.SlotCenter(r), _anim);
                    if (_seatAnchors.TryGetValue(place.Player, out Control seat) && seat.Bounds.Width > 0)
                        return seat.TranslatePoint(new Point(seat.Bounds.Width * 0.5, seat.Bounds.Height * 0.5), _anim);
                    return null;
                default: return null;
            }
        }

        /// <summary>
        /// Diffs the game against what we last saw and hands the differences to the animation layer. This is
        /// the only link between game state and animation, and it reads the state the same way an online
        /// client would read a server snapshot.
        /// </summary>
        void ObserveGame()
        {
            Game g = _c.Game;
            if (g == null) return;

            if (_viewGame != _c.GameNumber)
            {
                _viewGame = _c.GameNumber;
                _view = GameView.Capture(g, _c.Actor);
                _afterHandoff.Clear();
                Dispatcher.UIThread.Post(_anim.Clear);
                return;
            }

            GameView next = GameView.Capture(g, _c.Actor);
            List<VisualEvent> events = VisualDiff.Between(_view, next);
            _view = next;

            // While the device is being passed, hold the turn banner back until the next player is looking.
            if (_c.HandoffPending)
            {
                _afterHandoff.AddRange(events.Where(e => e is TurnStarted));
                events.RemoveAll(e => e is TurnStarted);
            }
            else if (_afterHandoff.Count > 0)
            {
                events.InsertRange(0, _afterHandoff);
                _afterHandoff.Clear();
            }

            if (events.Count > 0) Dispatcher.UIThread.Post(() => _anim.Play(events));
        }

        // ---- Status and log ------------------------------------------------------------------------

        void BuildStatus()
        {
            _status.Children.Clear();
            Game g = _c.Game;
            if (g == null) return;

            Player me = _c.ActorPlayer;
            _status.Children.Add(Ui.Row(8, Ui.Dot(Palette.Player(me.Id), 16), Ui.Text(me.Name, 20, true), Ui.Text($"turn {g.Turn}", 14, false, Ui.Muted)));
            _status.Children.Add(Ui.Text(_c.Prompt(), 14, false, Color.FromRgb(0xd8, 0xde, 0xea)));
            if (g.LastRoll > 0 && g.Phase != Phase.Roll)
                _status.Children.Add(Ui.Text($"Last roll: {g.LastRoll}", 14, true));

            _status.Children.Add(new Border { Height = 8 });
            _seatAnchors.Clear();
            foreach (Player p in g.Players)
            {
                bool mine = p.Id == me.Id;
                bool turn = p.Id == g.CurrentPlayer && g.Phase != Phase.GameOver;
                int vp = mine ? g.VictoryPoints(p.Id) : g.PublicVictoryPoints(p.Id);
                string badges = (g.GreatRoadHolder == p.Id ? "  Great Road" : "") + (g.GrandArmyHolder == p.Id ? "  Grand Army" : "");
                var line = Ui.Column(1,
                    Ui.Row(8, Ui.Dot(Palette.Player(p.Id)), Ui.Text(p.Name, 14, mine), Ui.Text($"{vp}/{g.Rules.VictoryPoints} VP", 14, true, Palette.Highlight)),
                    Ui.Text($"cards {p.HandCount}   actions {p.ActionCardCount}   soldiers {p.SoldiersPlayed}   road {p.GreatRoad}{badges}", 12, false, Ui.Muted));
                // The seat whose turn it is gets a tinted card with an edge in their color.
                Color pc = Palette.Player(p.Id);
                var seat = new Border
                {
                    Child = line,
                    Padding = new Thickness(8, 5),
                    CornerRadius = new CornerRadius(8),
                    Background = Palette.Brush(turn ? Color.FromArgb(40, pc.R, pc.G, pc.B) : Colors.Transparent),
                    BorderBrush = Palette.Brush(turn ? pc : Colors.Transparent),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                };
                _seatAnchors[p.Id] = seat;
                _status.Children.Add(seat);
            }

            _status.Children.Add(new Border { Height = 8 });
            _status.Children.Add(Ui.Text("Your hand", 15, true));
            _hand.Show(me.Hand, me.ActionCardCount, "Action");
            _status.Children.Add(_hand);

            string dev = string.Join(", ", ((ActionCard[])Enum.GetValues(typeof(ActionCard)))
                .Where(c => me.ActionCardsTotal(c) > 0)
                .Select(c => $"{c} x{me.ActionCardsTotal(c)}" + (me.ActionCardsUsable(c) < me.ActionCardsTotal(c) ? " (new)" : "")));
            _status.Children.Add(Ui.Text("Action cards: " + (dev.Length == 0 ? "none" : dev), 13, false, Ui.Muted));

            _status.Children.Add(new Border { Height = 8 });
            _status.Children.Add(Ui.Text("Bank", 13, true, Ui.Muted));
            _bankRow.Show(g.Bank, g.DevDeckCount, "Deck");
            _status.Children.Add(_bankRow);
        }

        void BuildLog()
        {
            _log.Children.Clear();
            _log.Children.Add(Ui.Text(_c.IsOnline ? "Game log and chat" : "Game log", 15, true));
            if (_chatBox.Parent is Control chatRow) chatRow.IsVisible = _c.IsOnline;
            foreach (string line in _c.Log.Skip(Math.Max(0, _c.Log.Count - 30)))
                _log.Children.Add(Ui.Text(line, 12, false, Color.FromRgb(0xc8, 0xcf, 0xdc)));
            Dispatcher.UIThread.Post(() => _logScroll.ScrollToEnd(), DispatcherPriority.Background);
        }

        // ---- Bottom action bar ---------------------------------------------------------------------

        void BuildBottom()
        {
            _bottom.Children.Clear();
            Game g = _c.Game;
            if (g == null) return;

            if (g.PendingTrade != null && g.Phase == Phase.Main) _bottom.Children.Add(BuildTradeOffer(g));

            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            void Add(Control c)
            {
                c.Margin = new Thickness(0, 0, 8, 6);
                buttons.Children.Add(c);
            }

            Player me = g.Players[g.CurrentPlayer];
            // Online, the action buttons only appear on our own turn; otherwise the bar shows the prompt (the default case).
            bool waiting = _c.IsOnline && g.CurrentPlayer != _c.MySeat && g.Phase != Phase.GameOver;
            switch (waiting ? Phase.Discard : g.Phase)
            {
                case Phase.Roll:
                    Add(Ui.Button("Roll dice", () => _c.Send(new RollDice(g.CurrentPlayer)), primary: true, minWidth: 130));
                    if (me.ActionCardsUsable(ActionCard.Soldier) > 0)
                        Add(Ui.Button("Play Soldier first", () => _c.Send(new PlaySoldier(g.CurrentPlayer))));
                    break;

                case Phase.Steal:
                    foreach (int id in g.StealCandidates)
                        Add(Ui.Button($"Steal from {g.Players[id].Name} ({g.Players[id].HandCount} cards)", () => _c.Send(new StealFrom(g.CurrentPlayer, id))));
                    break;

                case Phase.Main:
                    Add(Ui.Choice("Road\n1 Timber, 1 Clay", _c.Tool == Tool.Road, () => _c.SelectTool(Tool.Road), me.RoadsLeft > 0));
                    Add(Ui.Choice("Village\nTimber Clay Livestock Grain", _c.Tool == Tool.Village, () => _c.SelectTool(Tool.Village), me.VillagesLeft > 0));
                    Add(Ui.Choice("City\n2 Grain, 3 Iron", _c.Tool == Tool.City, () => _c.SelectTool(Tool.City), me.CitiesLeft > 0));
                    Add(Ui.Button("Buy action card", () => _c.Send(new BuyActionCard(me.Id)), me.Hand.Contains(Costs.ActionCard) && g.DevDeckCount > 0));
                    Add(Ui.Button("Bank trade", () => { _bankGive = _bankGet = Resource.Wasteland; OpenModal(Modal.BankTrade); }));
                    Add(Ui.Button("Trade with players", () =>
                    {
                        Array.Clear(_offerGive, 0, 5);
                        Array.Clear(_offerWant, 0, 5);
                        OpenModal(Modal.PlayerTrade);
                    }));
                    Add(Ui.Button("Play card", () => OpenModal(Modal.PlayCard)));
                    Add(Ui.Button("End turn", () => _c.Send(new EndTurn(me.Id)), primary: true, minWidth: 110));
                    break;

                case Phase.GameOver:
                    if (_c.IsOnline) Add(Ui.Button("Leave game", LeaveOnline, primary: true));
                    else Add(Ui.Button("New game", () => OpenModal(Modal.Setup), primary: true));
                    break;

                default:
                    Add(Ui.Text(_c.Prompt(), 14, false, Color.FromRgb(0xd8, 0xde, 0xea)));
                    break;
            }

            var spacer = new Border { Width = 24 };
            Add(spacer);
            // Online, only the host may change the rules.
            if (!_c.IsOnline || _c.Online.IsHost)
                Add(Ui.Button("House rules", () =>
                {
                    _draft = g.Rules.Clone();
                    OpenModal(Modal.Rules);
                }));
            if (_c.IsOnline && g.Phase != Phase.GameOver) Add(Ui.Button("Leave game", LeaveOnline));
            else Add(Ui.Button("New game", () => OpenModal(_offlineOnly ? Modal.Setup : Modal.Start)));
            _bottom.Children.Add(buttons);
        }

        Control BuildTradeOffer(Game g)
        {
            TradeOffer offer = g.PendingTrade;
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = $"{g.Players[offer.From].Name} offers {Ui.ResourceCounts(offer.Give)} for {Ui.ResourceCounts(offer.Want)}",
                Foreground = Palette.Brush(Colors.White),
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
            });
            foreach (Player p in g.Players)
            {
                if (p.Id == offer.From || (_c.IsOnline && p.Id != _c.MySeat)) continue;
                Player pl = p;
                Button b = Ui.Button($"Accept as {pl.Name}", () => _c.Send(new AcceptTrade(pl.Id)), pl.Hand.Contains(offer.Want));
                b.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(b);
            }
            if (!_c.IsOnline || offer.From == _c.MySeat)
                row.Children.Add(Ui.Button("Withdraw", () => _c.Send(new CancelTrade(offer.From))));
            return new Border
            {
                Background = Palette.Brush(Color.FromRgb(0x2e, 0x34, 0x40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = row,
            };
        }
    }
}
