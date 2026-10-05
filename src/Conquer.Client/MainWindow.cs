using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Conquer.Client.Animation;
using Conquer.Core;

namespace Conquer.Client
{
    public sealed partial class MainWindow : Window
    {
        enum Modal { Start, Online, Lan, Setup, BoardSetup, None, BankTrade, PlayerTrade, PlayCard, PickHarvest, PickPlunder, Rules }

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
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Conquer/Assets/conquer.ico")));
            Width = 1360;
            Height = 860;
            MinWidth = 1080;
            MinHeight = 700;
            Background = Palette.Brush(Palette.Window);

            _board.Controller = _c;
            _c.Changed += ObserveGame;
            _c.Changed += () => Dispatcher.UIThread.Post(Rebuild);
            SetUpAnimations();
            SetUpBoardPreview();
            _c.ToastShown += ShowToast;
            _toastTimer.Tick += (_, _) =>
            {
                _toast.Text = "";
                _toastTimer.Stop();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && EscapeBack()) e.Handled = true;
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
            var main = _main = new Grid
            {
                Background = Palette.Brush(Palette.Window),
                ColumnDefinitions = new ColumnDefinitions("340,*,320"),
                RowDefinitions = new RowDefinitions("*,Auto"),
            };

            var statusScroll = new Border
            {
                Background = Palette.Brush(Palette.Side),
                BorderBrush = Palette.Brush(Palette.SideEdge),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { Content = new Border { Padding = new Thickness(16, 14), Child = _status } },
            };
            Grid.SetColumn(statusScroll, 0);

            var boardArea = new Grid();
            boardArea.Children.Add(_board);
            boardArea.Children.Add(_results);
            var boardHost = new Border { Margin = new Thickness(0), Child = boardArea, ClipToBounds = true };
            Grid.SetColumn(boardHost, 1);

            _logScroll.Content = new Border { Padding = new Thickness(16, 14), Child = _log };
            var logPanel = new DockPanel();
            Control chat = BuildChatRow();
            DockPanel.SetDock(chat, Dock.Bottom);
            logPanel.Children.Add(chat);
            logPanel.Children.Add(_logScroll);
            var logColumn = new Border
            {
                Background = Palette.Brush(Palette.Side),
                BorderBrush = Palette.Brush(Palette.SideEdge),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = logPanel,
            };
            Grid.SetColumn(logColumn, 2);

            var bottomBar = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Palette.PanelTop, 0), new GradientStop(Palette.Panel, 1) },
                },
                BorderBrush = Palette.Brush(Palette.PanelEdge),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
                Child = _bottom,
            };
            Grid.SetRow(bottomBar, 1);
            Grid.SetColumnSpan(bottomBar, 3);

            main.Children.Add(statusScroll);
            main.Children.Add(boardHost);
            main.Children.Add(logColumn);
            main.Children.Add(bottomBar);

            var root = new Grid();
            root.Children.Add(_backdrop);
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
            BuildResults();
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
            var name = Ui.Text(me.Name, 21, true, Palette.Text);
            name.VerticalAlignment = VerticalAlignment.Center;
            var header = new DockPanel();
            Control turnChip = Chip($"Turn {g.Turn}", Ui.Muted, Palette.SideRaised);
            DockPanel.SetDock(turnChip, Dock.Right);
            header.Children.Add(turnChip);
            header.Children.Add(Ui.Row(10, Ui.Dot(Palette.Player(me.Id), 18), name));
            _status.Children.Add(header);

            // What to do now, in a callout with a gold edge, and the last roll beside it.
            var prompt = Ui.Text(_c.Prompt(), 14, false, Palette.Text);
            var callout = Ui.Column(4, prompt);
            if (g.LastRoll > 0 && g.Phase != Phase.Roll)
                callout.Children.Add(Ui.Colored(new[] { ("Last roll  ", Ui.Muted, false), (g.LastRoll.ToString(), Palette.Gold, true) }, 13));
            _status.Children.Add(new Border
            {
                Child = callout,
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(12, 9),
                CornerRadius = new CornerRadius(0, 8, 8, 0),
                Background = Palette.Brush(Color.FromArgb(0x1c, Palette.Gold.R, Palette.Gold.G, Palette.Gold.B)),
                BorderBrush = Palette.Brush(Palette.Gold),
                BorderThickness = new Thickness(3, 0, 0, 0),
            });

            _status.Children.Add(Ui.Section("Players"));
            _seatAnchors.Clear();
            foreach (Player p in g.Players)
            {
                bool mine = p.Id == me.Id;
                bool turn = p.Id == g.CurrentPlayer && g.Phase != Phase.GameOver;
                int vp = mine ? g.VictoryPoints(p.Id) : g.PublicVictoryPoints(p.Id);
                Color pc = Palette.Player(p.Id);

                var top = new DockPanel();
                Control points = Chip($"{vp}/{g.Rules.VictoryPoints} VP", Palette.Gold, Color.FromArgb(0x26, Palette.Gold.R, Palette.Gold.G, Palette.Gold.B));
                DockPanel.SetDock(points, Dock.Right);
                top.Children.Add(points);
                var who = Ui.Text(p.Name, 14, mine, Palette.Text);
                who.VerticalAlignment = VerticalAlignment.Center;
                top.Children.Add(Ui.Row(8, Ui.Dot(pc), who));

                var line = Ui.Column(4, top,
                    Ui.Text($"{p.HandCount} cards  ·  {p.ActionCardCount} actions  ·  {p.SoldiersPlayed} soldiers  ·  road {p.GreatRoad}", 12, false, Ui.Muted));
                if (g.GreatRoadHolder == p.Id || g.GrandArmyHolder == p.Id)
                {
                    var badges = Ui.Row(6);
                    if (g.GreatRoadHolder == p.Id) badges.Children.Add(Chip("Great Road", Palette.Text, Palette.Darken(Palette.GoldDeep, 0.75)));
                    if (g.GrandArmyHolder == p.Id) badges.Children.Add(Chip("Grand Army", Palette.Text, Palette.Darken(Palette.GoldDeep, 0.75)));
                    line.Children.Add(badges);
                }

                // The seat whose turn it is gets a tinted card with an edge in their color.
                var seat = new Border
                {
                    Child = line,
                    Padding = new Thickness(10, 7),
                    CornerRadius = new CornerRadius(8),
                    Background = Palette.Brush(turn ? Color.FromArgb(44, pc.R, pc.G, pc.B) : Palette.SideRaised),
                    BorderBrush = Palette.Brush(turn ? pc : Palette.SideRaised),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                };
                _seatAnchors[p.Id] = seat;
                _status.Children.Add(seat);
            }

            _status.Children.Add(Ui.Section("Your hand"));
            _hand.Show(me.Hand, me.ActionCardCount, "Action");
            _status.Children.Add(_hand);

            string dev = string.Join(", ", ((ActionCard[])Enum.GetValues(typeof(ActionCard)))
                .Where(c => me.ActionCardsTotal(c) > 0)
                .Select(c => $"{c} x{me.ActionCardsTotal(c)}" + (me.ActionCardsUsable(c) < me.ActionCardsTotal(c) ? " (new)" : "")));
            _status.Children.Add(Ui.Text("Action cards: " + (dev.Length == 0 ? "none" : dev), 13, false, Ui.Muted));

            _status.Children.Add(Ui.Section("Bank"));
            _bankRow.Show(g.Bank, g.DevDeckCount, "Deck");
            _status.Children.Add(_bankRow);
        }

        /// <summary>A small rounded label, for points, turn numbers and badges.</summary>
        static Control Chip(string text, Color fg, Color bg) => new Border
        {
            Background = Palette.Brush(bg),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Palette.Brush(fg) },
        };

        void BuildLog()
        {
            _log.Children.Clear();
            _log.Children.Add(Ui.Section(_c.IsOnline ? "Game log and chat" : "Game log"));
            _log.Children.Add(new Border { Height = 4 });
            if (_chatBox.Parent is Control chatRow) chatRow.IsVisible = _c.IsOnline;
            int from = Math.Max(0, _c.Log.Count - 30);
            for (int i = from; i < _c.Log.Count; i++)
            {
                // Older lines step back so the latest news stands out.
                bool latest = i == _c.Log.Count - 1;
                var line = Ui.Text(_c.Log[i], 12, latest, latest ? Palette.Text : Color.FromRgb(0xb4, 0xbc, 0xca));
                line.Margin = new Thickness(0, 1);
                _log.Children.Add(line);
            }
            Dispatcher.UIThread.Post(() => _logScroll.ScrollToEnd(), DispatcherPriority.Background);
        }

        // ---- Bottom action bar ---------------------------------------------------------------------

        void BuildBottom()
        {
            _bottom.Children.Clear();
            Game g = _c.Game;
            if (g == null) return;

            if (g.PendingTrade != null && g.Phase == Phase.Main) _bottom.Children.Add(BuildTradeOffer(g));

            // Actions for this moment of the turn on the left; the game menu (rules, new game) tucked to the right.
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void Add(Control c, double gap = 6)
            {
                c.Margin = new Thickness(0, 3, gap, 3);
                if (c is Button b)
                {
                    b.MinHeight = 50;
                    b.VerticalContentAlignment = VerticalAlignment.Center;
                }
                else c.VerticalAlignment = VerticalAlignment.Center;
                buttons.Children.Add(c);
            }

            Player me = g.Players[g.CurrentPlayer];
            // Online, the action buttons only appear on our own turn; otherwise the bar shows the prompt (the default case).
            bool waiting = _c.IsOnline && g.CurrentPlayer != _c.MySeat && g.Phase != Phase.GameOver;
            switch (waiting ? Phase.Discard : g.Phase)
            {
                case Phase.Roll:
                    Add(Ui.Button("Roll dice", () => _c.Send(new RollDice(g.CurrentPlayer)), primary: true, minWidth: 150));
                    if (me.ActionCardsUsable(ActionCard.Soldier) > 0)
                        Add(Ui.Button("Play Soldier first", () => _c.Send(new PlaySoldier(g.CurrentPlayer))));
                    break;

                case Phase.Steal:
                    foreach (int id in g.StealCandidates)
                        Add(Ui.Button($"Steal from {g.Players[id].Name} ({g.Players[id].HandCount} cards)", () => _c.Send(new StealFrom(g.CurrentPlayer, id))));
                    break;

                case Phase.Main:
                    Color mine = Palette.Player(me.Id);
                    Add(BuildButton("Road", PieceIcon.Road, mine, Costs.Road, Tool.Road, me.RoadsLeft, "roads"));
                    Add(BuildButton("Village", PieceIcon.Village, mine, Costs.Village, Tool.Village, me.VillagesLeft, "villages"));
                    Add(BuildButton("City", PieceIcon.City, mine, Costs.City, Tool.City, me.CitiesLeft, "cities"), 18);
                    Button buy = Ui.Button("Buy action card", () => _c.Send(new BuyActionCard(me.Id)), me.Hand.Contains(Costs.ActionCard) && g.DevDeckCount > 0);
                    ToolTip.SetTip(buy, $"Costs {Costs.ActionCard.Describe()}. {g.DevDeckCount} left in the deck.");
                    Add(buy);
                    Add(Ui.Button("Bank trade", () => { _bankGive = _bankGet = Resource.Wasteland; OpenModal(Modal.BankTrade); }));
                    Add(Ui.Button("Trade with players", () =>
                    {
                        Array.Clear(_offerGive, 0, 5);
                        Array.Clear(_offerWant, 0, 5);
                        OpenModal(Modal.PlayerTrade);
                    }));
                    Add(Ui.Button("Play card", () => OpenModal(Modal.PlayCard)), 18);
                    Add(Ui.Button("End turn", () => _c.Send(new EndTurn(me.Id)), primary: true, minWidth: 120));
                    break;

                case Phase.GameOver:
                    if (_c.IsOnline) Add(Ui.Button("Leave game", LeaveOnline, primary: true, minWidth: 150));
                    else Add(Ui.Button("New game", () => OpenModal(Modal.Setup), primary: true, minWidth: 150));
                    break;

                default:
                    var prompt = Ui.Text(_c.Prompt(), 15, true, Palette.Text);
                    prompt.Margin = new Thickness(4, 0, 0, 0);
                    Add(prompt);
                    break;
            }

            // Online, only the host may change the rules.
            var menu = Ui.Row(6);
            menu.VerticalAlignment = VerticalAlignment.Center;
            void Menu(Button b)
            {
                b.Classes.Add(GameTheme.Quiet);
                b.Padding = new Thickness(12, 7);
                menu.Children.Add(b);
            }
            if (!_c.IsOnline || _c.Online.IsHost)
                Menu(Ui.Button("House rules", () =>
                {
                    _draft = g.Rules.Clone();
                    OpenModal(Modal.Rules);
                }));
            if (_c.IsOnline && g.Phase != Phase.GameOver) Menu(Ui.Button("Leave game", LeaveOnline));
            else if (_c.IsOnline || g.Phase != Phase.GameOver) Menu(Ui.Button("New game", () => OpenModal(_offlineOnly ? Modal.Setup : Modal.Start)));

            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            bar.Children.Add(buttons);
            if (menu.Children.Count > 0)
            {
                var divider = new Border { Width = 1, Margin = new Thickness(10, 8, 12, 8), Background = Palette.Brush(Palette.PanelEdge) };
                var right = Ui.Row(0, divider, menu);
                Grid.SetColumn(right, 1);
                bar.Children.Add(right);
            }
            _bottom.Children.Add(bar);
        }

        /// <summary>
        /// A build tool button: the piece in the player's color, its name, and its cost as colored card pips. The
        /// full cost and the pieces left are in the tooltip.
        /// </summary>
        Button BuildButton(string name, PieceIcon icon, Color color, ResourceSet cost, Tool tool, int left, string plural)
        {
            var pips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (Resource r in ResourceSet.Types)
            {
                for (int i = 0; i < cost[r]; i++)
                {
                    Color c = Palette.Resource(r);
                    pips.Children.Add(new Border
                    {
                        Width = 9,
                        Height = 12,
                        CornerRadius = new CornerRadius(2),
                        Background = Palette.Brush(c),
                        BorderBrush = Palette.Brush(Palette.Darken(c, 0.45)),
                        BorderThickness = new Thickness(1),
                    });
                }
            }
            var label = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var top = Ui.Row(7, icon.Draw(color), label);
            top.HorizontalAlignment = HorizontalAlignment.Center;

            var b = new Button
            {
                Content = Ui.Column(5, top, pips),
                IsEnabled = left > 0,
                Padding = new Thickness(14, 6),
                MinWidth = 100,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (_c.Tool == tool) b.Classes.Add(GameTheme.Selected);
            b.Click += (_, _) => _c.SelectTool(tool);
            ToolTip.SetTip(b, $"{name}: {cost.Describe()}. You have {left} {plural} left.");
            return b;
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
