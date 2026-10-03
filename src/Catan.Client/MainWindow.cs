using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Catan.Core;

namespace Catan.Client
{
    public sealed partial class MainWindow : Window
    {
        enum Modal { Setup, None, BankTrade, PlayerTrade, PlayCard, PickYearOfPlenty, PickMonopoly, Rules }

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

        Modal _modal = Modal.Setup;

        // Dialog working state
        int _setupPlayers = 3, _setupRadius = 2, _setupVp = 10;
        bool _setupHide = true;
        Resource _bankGive = Resource.Desert, _bankGet = Resource.Desert;
        readonly int[] _offerGive = new int[5], _offerWant = new int[5], _discardSel = new int[5];
        int _discardFor = -1;
        Resource _yearFirst = Resource.Desert;
        HouseRules _draft;

        public MainWindow()
        {
            Title = "Catan";
            Width = 1360;
            Height = 860;
            MinWidth = 1080;
            MinHeight = 700;
            Background = Palette.Brush(Color.FromRgb(0x14, 0x17, 0x1c));

            _board.Controller = _c;
            _c.Changed += () => Dispatcher.UIThread.Post(Rebuild);
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
            Grid.SetColumn(_logScroll, 2);

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
            main.Children.Add(_logScroll);
            main.Children.Add(bottomBar);

            var root = new Grid();
            root.Children.Add(main);
            root.Children.Add(_toast);
            root.Children.Add(_overlay);
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
            foreach (Player p in g.Players)
            {
                bool mine = p.Id == me.Id;
                int vp = mine ? g.VictoryPoints(p.Id) : g.PublicVictoryPoints(p.Id);
                string badges = (g.LongestRoadHolder == p.Id ? "  Longest Road" : "") + (g.LargestArmyHolder == p.Id ? "  Largest Army" : "");
                var line = Ui.Column(1,
                    Ui.Row(8, Ui.Dot(Palette.Player(p.Id)), Ui.Text(p.Name, 14, mine), Ui.Text($"{vp}/{g.Rules.VictoryPoints} VP", 14, true, Palette.Highlight)),
                    Ui.Text($"cards {p.HandCount}   dev {p.DevCardCount}   knights {p.KnightsPlayed}   road {p.LongestRoad}{badges}", 12, false, Ui.Muted));
                _status.Children.Add(line);
            }

            _status.Children.Add(new Border { Height = 8 });
            _status.Children.Add(Ui.Text("Your hand", 15, true));
            _status.Children.Add(Ui.ResourceLine(me.Hand, 13));

            string dev = string.Join(", ", ((DevCard[])Enum.GetValues(typeof(DevCard)))
                .Where(c => me.DevCardsTotal(c) > 0)
                .Select(c => $"{c} x{me.DevCardsTotal(c)}" + (me.DevCardsUsable(c) < me.DevCardsTotal(c) ? " (new)" : "")));
            _status.Children.Add(Ui.Text("Dev cards: " + (dev.Length == 0 ? "none" : dev), 13, false, Ui.Muted));

            _status.Children.Add(new Border { Height = 8 });
            _status.Children.Add(Ui.Text($"Bank: {Ui.ResourceCounts(g.Bank)}", 12, false, Ui.Muted));
            _status.Children.Add(Ui.Text($"Development deck: {g.DevDeckCount} cards", 12, false, Ui.Muted));
        }

        void BuildLog()
        {
            _log.Children.Clear();
            _log.Children.Add(Ui.Text("Game log", 15, true));
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
            switch (g.Phase)
            {
                case Phase.Roll:
                    Add(Ui.Button("Roll dice", () => _c.Send(new RollDice(g.CurrentPlayer)), primary: true, minWidth: 130));
                    if (me.DevCardsUsable(DevCard.Knight) > 0)
                        Add(Ui.Button("Play Knight first", () => _c.Send(new PlayKnight(g.CurrentPlayer))));
                    break;

                case Phase.Steal:
                    foreach (int id in g.StealCandidates)
                        Add(Ui.Button($"Steal from {g.Players[id].Name} ({g.Players[id].HandCount} cards)", () => _c.Send(new StealFrom(g.CurrentPlayer, id))));
                    break;

                case Phase.Main:
                    Add(Ui.Choice("Road\n1 Wood, 1 Brick", _c.Tool == Tool.Road, () => _c.SelectTool(Tool.Road), me.RoadsLeft > 0));
                    Add(Ui.Choice("Settlement\nWood Brick Sheep Wheat", _c.Tool == Tool.Settlement, () => _c.SelectTool(Tool.Settlement), me.SettlementsLeft > 0));
                    Add(Ui.Choice("City\n2 Wheat, 3 Ore", _c.Tool == Tool.City, () => _c.SelectTool(Tool.City), me.CitiesLeft > 0));
                    Add(Ui.Button("Buy dev card", () => _c.Send(new BuyDevCard(me.Id)), me.Hand.Contains(Costs.DevCard) && g.DevDeckCount > 0));
                    Add(Ui.Button("Bank trade", () => { _bankGive = _bankGet = Resource.Desert; OpenModal(Modal.BankTrade); }));
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
                    Add(Ui.Button("New game", () => OpenModal(Modal.Setup), primary: true));
                    break;

                default:
                    Add(Ui.Text(_c.Prompt(), 14, false, Color.FromRgb(0xd8, 0xde, 0xea)));
                    break;
            }

            var spacer = new Border { Width = 24 };
            Add(spacer);
            Add(Ui.Button("House rules", () =>
            {
                _draft = g.Rules.Clone();
                OpenModal(Modal.Rules);
            }));
            Add(Ui.Button("New game", () => OpenModal(Modal.Setup)));
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
                if (p.Id == offer.From) continue;
                Player pl = p;
                Button b = Ui.Button($"Accept as {pl.Name}", () => _c.Send(new AcceptTrade(pl.Id)), pl.Hand.Contains(offer.Want));
                b.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(b);
            }
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
