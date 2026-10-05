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
        enum Modal { Start, Online, Setup, None, BankTrade, PlayerTrade, PlayCard, PickHarvest, PickPlunder, Rules }

        readonly LocalGameController _c = new LocalGameController();
        readonly BoardControl _board = new BoardControl();
        readonly StackPanel _status = new StackPanel { Spacing = 8 };
        readonly StackPanel _log = new StackPanel { Spacing = 2 };
        readonly ScrollViewer _logScroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        readonly StackPanel _bottom = new StackPanel { Spacing = 8 };
        readonly Grid _overlay = new Grid { IsVisible = false, Name = "Overlay" };
        readonly TextBlock _toast = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Black,
            Foreground = Palette.Brush(Colors.White),
        };
        readonly Border _toastPill = new Border
        {
            Background = Palette.Brush(Color.FromRgb(0xe8, 0x4a, 0x3c)),
            BorderBrush = Palette.Brush(Color.FromRgb(0x8a, 0x1e, 0x14)),
            BorderThickness = new Thickness(2, 2, 2, 4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
            IsVisible = false,
            BoxShadow = BoxShadows.Parse("0 6 16 0 #50000000"),
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
            MinWidth = 960;
            MinHeight = 640;
            Background = Palette.Brush(Palette.Window);
            FontFamily = Ui.Font;

            _board.Controller = _c;
            _c.Changed += ObserveGame;
            _c.Changed += () => Dispatcher.UIThread.Post(Rebuild);
            SetUpAnimations();
            _c.ToastShown += ShowToast;
            _toastTimer.Tick += (_, _) =>
            {
                _toast.Text = "";
                _toastPill.IsVisible = false;
                _toastTimer.Stop();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && EscapeBack()) e.Handled = true;
            };

            Content = BuildShell();
            SizeChanged += (_, e) => Relayout(e.NewSize.Width);
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

        // The game screen: player cards and the bank on the left, the board in a rounded sea frame in the
        // middle with the prompt floating over its corner, the log on the right (it folds away), and a wooden
        // tray along the bottom with the hand fanned out next to the action buttons.
        readonly Border _left = new Border();
        readonly Border _right = new Border();
        readonly Border _prompt = new Border { Name = "Prompt" };
        readonly StackPanel _handBox = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Bottom };
        readonly StackPanel _logHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Control _chatRow;
        bool _logFolded;

        Control BuildShell()
        {
            var main = _main = new Grid
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Palette.Sky, 0), new GradientStop(Palette.SkyDeep, 1) },
                },
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                RowDefinitions = new RowDefinitions("*,Auto"),
            };

            _left.Margin = new Thickness(10, 10, 0, 10);
            var leftDock = new DockPanel();
            DockPanel.SetDock(_menu, Dock.Bottom);
            leftDock.Children.Add(_menu);
            leftDock.Children.Add(new ScrollViewer
            {
                Content = _status,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            });
            _left.Child = leftDock;
            Grid.SetColumn(_left, 0);

            // The board sits in a sea-blue frame with a thick outline, like a game board on a table.
            var boardArea = new Grid();
            boardArea.Children.Add(_board);
            _prompt.HorizontalAlignment = HorizontalAlignment.Left;
            _prompt.VerticalAlignment = VerticalAlignment.Top;
            _prompt.Margin = new Thickness(12);
            boardArea.Children.Add(_prompt);
            boardArea.Children.Add(_results);
            var boardHost = new Border
            {
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(26),
                BorderBrush = Palette.Brush(Color.FromRgb(0x1b, 0x5c, 0x8c)),
                BorderThickness = new Thickness(4),
                BoxShadow = BoxShadows.Parse("0 6 0 0 #40103050"),
                Background = Palette.Brush(Palette.Sea),
                Child = new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = boardArea },
            };
            Grid.SetColumn(boardHost, 1);

            _logScroll.Content = _log;
            var logPanel = new DockPanel();
            _logHeader.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(_logHeader, Dock.Top);
            logPanel.Children.Add(_logHeader);
            _chatRow = BuildChatRow();
            DockPanel.SetDock(_chatRow, Dock.Bottom);
            logPanel.Children.Add(_chatRow);
            logPanel.Children.Add(_logScroll);
            _right.Margin = new Thickness(0, 10, 10, 10);
            _right.Child = Ui.Panel(logPanel, new Thickness(12, 10, 8, 10));
            Grid.SetColumn(_right, 2);

            var tray = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Palette.WoodLight, 0), new GradientStop(Palette.Wood, 0.5), new GradientStop(Palette.Darken(Palette.Wood, 0.85), 1) },
                },
                BorderBrush = Palette.Brush(Palette.WoodDeep),
                BorderThickness = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(14, 8, 14, 10),
                Child = _bottom,
            };
            Grid.SetRow(tray, 1);
            Grid.SetColumnSpan(tray, 3);

            main.Children.Add(_left);
            main.Children.Add(boardHost);
            main.Children.Add(_right);
            main.Children.Add(tray);

            _hand.Fan = true;
            _hand.LabelColor = Color.FromRgb(0xff, 0xf3, 0xd6);
            _toastPill.Child = _toast;

            var root = new Grid();
            root.Children.Add(_backdrop);
            root.Children.Add(main);
            root.Children.Add(_anim);
            root.Children.Add(_overlay);
            root.Children.Add(_toastPill); // above dialogs, so lobby and connect errors show too
            Relayout(Width);
            return root;
        }

        /// <summary>Side panels and cards scale with the window, so the board stays big on small screens.</summary>
        void Relayout(double width)
        {
            if (double.IsNaN(width) || width <= 0) width = 1360;
            double left = Math.Clamp(width * 0.165, 188, 262);
            _left.Width = left;
            _right.Width = _logFolded ? 64 : Math.Clamp(width * 0.18, 188, 290);
            _hand.SetCardSize(Math.Clamp(width * 0.036, 40, 54), Math.Clamp(width * 0.007, 6, 10));
            // The bank's six stacks share the left column.
            double inner = left - 30;
            double bankCard = Math.Clamp((inner - 14) / 6.6, 20, 34);
            _bankRow.SetCardSize(bankCard, (inner - 14 - 6 * bankCard) / 5);
        }

                // ---- Rebuilding the screen from state ------------------------------------------------------

        void Rebuild()
        {
            BuildStatus();
            BuildPrompt();
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
                _toastPill.IsVisible = !string.IsNullOrEmpty(message);
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
                if (place.Kind == PlaceKind.Player && place.Player == _c.Viewer) _hand.Bump(r);
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
                    if (_c.Game != null && place.Player == _c.Viewer && _hand.IsVisible && _hand.Bounds.Width > 0)
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
                _view = GameView.Capture(g, _c.Viewer);
                _afterHandoff.Clear();
                Dispatcher.UIThread.Post(_anim.Clear);
                return;
            }

            GameView next = GameView.Capture(g, _c.Viewer);
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

        // ---- Left column: players, bank, game menu -------------------------------------------------

        static void Detach(Control c)
        {
            if (c.Parent is Panel p) p.Children.Remove(c);
            else if (c.Parent is Decorator d) d.Child = null;
        }

        readonly StackPanel _menu = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

        void BuildStatus()
        {
            _status.Children.Clear();
            _menu.Children.Clear();
            Game g = _c.Game;
            if (g == null) return;

            Player me = _c.ViewerPlayer;
            var header = new DockPanel { Margin = new Thickness(4, 0, 2, 0) };
            Control turnChip = Ui.Chip($"Turn {g.Turn}", Colors.White, Palette.SelectDeep);
            DockPanel.SetDock(turnChip, Dock.Right);
            header.Children.Add(turnChip);
            var title = Ui.Text("Players", 17, true, Color.FromRgb(0x10, 0x3a, 0x5c));
            title.FontWeight = FontWeight.Black;
            header.Children.Add(title);
            _status.Children.Add(header);

            _seatAnchors.Clear();
            foreach (Player p in g.Players)
            {
                Control seat = SeatCard(g, p, p.Id == me.Id);
                _seatAnchors[p.Id] = seat;
                _status.Children.Add(seat);
            }

            Detach(_bankRow);
            _bankRow.Show(g.Bank, g.DevDeckCount, "Deck");
            _bankRow.HorizontalAlignment = HorizontalAlignment.Center;
            var bankTitle = Ui.Row(6, new Glyph(GlyphKind.Collect, Glyph.DefaultColor(GlyphKind.Collect), 15), Ui.Section("Bank"));
            bankTitle.Children[1].Margin = new Thickness(0);
            var bank = Ui.Panel(Ui.Column(2, bankTitle, _bankRow), new Thickness(8, 6, 8, 2));
            bank.Margin = new Thickness(0, 2, 0, 0);
            _status.Children.Add(bank);

            // The game menu. Online, only the host may change the rules.
            void Menu(Button b)
            {
                b.Classes.Add(GameTheme.Quiet);
                b.Padding = new Thickness(10, 6);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                _menu.Children.Add(b);
            }
            var menuRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") };
            if (!_c.IsOnline || _c.Online.IsHost)
                Menu(Ui.Button("House rules", () =>
                {
                    _draft = g.Rules.Clone();
                    OpenModal(Modal.Rules);
                }));
            if (_c.IsOnline && g.Phase != Phase.GameOver) Menu(Ui.Button("Leave game", LeaveOnline));
            else if (_c.IsOnline || g.Phase != Phase.GameOver) Menu(Ui.Button("New game", () => OpenModal(_offlineOnly ? Modal.Setup : Modal.Start)));
            // Two buttons share a row; one stretches across it.
            if (_menu.Children.Count == 2)
            {
                var a = _menu.Children[0];
                var b = _menu.Children[1];
                _menu.Children.Clear();
                Grid.SetColumn(b, 2);
                menuRow.Children.Add(a);
                menuRow.Children.Add(b);
                _menu.Children.Add(menuRow);
            }
        }

        /// <summary>
        /// A player's card: a round avatar in their colour, name (with a BOT tag for computer players), points as
        /// a star, and small icons for cards, action cards, soldiers and road length. The player whose turn it is
        /// gets a card tinted and outlined in their colour.
        /// </summary>
        Control SeatCard(Game g, Player p, bool mine)
        {
            bool turn = p.Id == g.CurrentPlayer && g.Phase != Phase.GameOver;
            int vp = mine ? g.VictoryPoints(p.Id) : g.PublicVictoryPoints(p.Id);
            Color pc = Palette.Player(p.Id);

            var avatar = Avatar(p, 32);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            avatar.Margin = new Thickness(0, 1, 8, 0);

            var who = new TextBlock
            {
                Text = p.Name,
                FontSize = 14,
                FontWeight = FontWeight.Black,
                Foreground = Palette.Brush(Palette.Text),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameRow = new DockPanel();
            var points = Ui.Row(2, new Glyph(GlyphKind.Star, Palette.Gold, 16), Ui.Text($"{vp}", 15, true, Palette.Text));
            ((TextBlock)points.Children[1]).FontWeight = FontWeight.Black;
            points.Children.Add(Ui.Text($"/{g.Rules.VictoryPoints}", 11, true, Ui.Muted));
            ((TextBlock)points.Children[2]).VerticalAlignment = VerticalAlignment.Bottom;
            ((TextBlock)points.Children[2]).Margin = new Thickness(0, 0, 0, 2);
            ToolTip.SetTip(points, $"{vp} of {g.Rules.VictoryPoints} victory points");
            DockPanel.SetDock(points, Dock.Right);
            nameRow.Children.Add(points);
            if (_c.IsBot(p.Id))
            {
                Border bot = Ui.Chip("BOT", Colors.White, Color.FromRgb(0x7a, 0x86, 0x9a), 10);
                bot.Margin = new Thickness(4, 0, 4, 0);
                DockPanel.SetDock(bot, Dock.Right);
                nameRow.Children.Add(bot);
            }
            nameRow.Children.Add(who);

            var stats = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            void Stat(GlyphKind kind, int n, string tip)
            {
                var pair = Ui.Row(2, new Glyph(kind, Glyph.DefaultColor(kind), 13), Ui.Text(n.ToString(), 12, true, Palette.Text));
                pair.Margin = new Thickness(0, 0, 7, 0);
                ToolTip.SetTip(pair, tip);
                stats.Children.Add(pair);
            }
            Stat(GlyphKind.Cards, p.HandCount, $"{p.HandCount} resource cards");
            Stat(GlyphKind.Action, p.ActionCardCount, $"{p.ActionCardCount} action cards");
            Stat(GlyphKind.Soldier, p.SoldiersPlayed, $"{p.SoldiersPlayed} soldiers played");
            Stat(GlyphKind.Road, p.GreatRoad, $"Longest road: {p.GreatRoad}");

            var body = Ui.Column(0, nameRow, stats);
            if (g.GreatRoadHolder == p.Id || g.GrandArmyHolder == p.Id)
            {
                var badges = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                if (g.GreatRoadHolder == p.Id) badges.Children.Add(Badge("Great Road"));
                if (g.GrandArmyHolder == p.Id) badges.Children.Add(Badge("Grand Army"));
                body.Children.Add(badges);
            }

            var grid = new DockPanel();
            DockPanel.SetDock(avatar, Dock.Left);
            grid.Children.Add(avatar);
            grid.Children.Add(body);

            return new Border
            {
                Child = grid,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(16),
                Background = turn
                    ? new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Palette.Lighten(pc, 0.82), 0), new GradientStop(Palette.Lighten(pc, 0.6), 1) },
                    }
                    : Ui.Parchment(),
                BorderBrush = Palette.Brush(turn ? Palette.Darken(pc, pc.R > 0xe0 && pc.G > 0xe0 ? 0.55 : 0.8) : Palette.Outline),
                BorderThickness = new Thickness(turn ? 4 : 2.5),
                BoxShadow = BoxShadows.Parse(turn ? "0 5 0 0 #50204060" : "0 3 0 0 #35204060"),
                Margin = turn ? new Thickness(0, 0, -4, 0) : new Thickness(0, 0, 4, 0),
            };
        }

        static Control Badge(string text)
        {
            Border b = Ui.Chip(text, Palette.Ink, Palette.Gold, 10);
            b.BorderBrush = Palette.Brush(Palette.GoldDeep);
            b.BorderThickness = new Thickness(1.5);
            b.Margin = new Thickness(0, 0, 4, 0);
            return b;
        }

        /// <summary>A round token in the player's colour with their initial.</summary>
        static Control Avatar(Player p, double size)
        {
            Color pc = Palette.Player(p.Id);
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Palette.Lighten(pc, 0.35), 0), new GradientStop(pc, 0.7) },
                },
                BorderBrush = Palette.Brush(Palette.Ink),
                BorderThickness = new Thickness(2.5),
                Child = new TextBlock
                {
                    Text = p.Name.Length > 0 ? p.Name.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = size * 0.5,
                    FontWeight = FontWeight.Black,
                    Foreground = Palette.Brush(Palette.OnPlayer(p.Id)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        // ---- The prompt over the board -------------------------------------------------------------

        /// <summary>Whose move it is and what to do now, on a parchment note pinned to the board's corner.</summary>
        void BuildPrompt()
        {
            Game g = _c.Game;
            _prompt.IsVisible = g != null;
            if (g == null) return;

            Player mover = _c.ActorPlayer;
            var name = Ui.Text(mover.Name, 17, true, Palette.Text);
            name.FontWeight = FontWeight.Black;
            name.VerticalAlignment = VerticalAlignment.Center;
            var top = Ui.Row(8, Avatar(mover, 26), name);
            if (g.LastRoll > 0 && g.Phase != Phase.Roll)
            {
                var roll = Ui.Row(4, new Glyph(GlyphKind.Dice, Colors.White, 18), Ui.Text(g.LastRoll.ToString(), 15, true, Palette.Text));
                ((TextBlock)roll.Children[1]).FontWeight = FontWeight.Black;
                ToolTip.SetTip(roll, $"Last roll: {g.LastRoll}");
                var pill = new Border
                {
                    Child = roll,
                    Background = Palette.Brush(Palette.SideRaised),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 1, 8, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                top.Children.Add(pill);
            }
            var text = Ui.Text(_c.Prompt(), 14, true, Palette.Text);
            text.Margin = new Thickness(2, 4, 0, 0);
            Color pc = Palette.Player(mover.Id);
            _prompt.Child = Ui.Column(0, top, text);
            _prompt.MaxWidth = 330;
            _prompt.Padding = new Thickness(12, 8, 14, 10);
            _prompt.CornerRadius = new CornerRadius(18);
            _prompt.Background = Ui.Parchment();
            _prompt.BorderBrush = Palette.Brush(Palette.Outline);
            _prompt.BorderThickness = new Thickness(3, 3, 3, 3);
            _prompt.BoxShadow = BoxShadows.Parse("0 5 0 0 #40103050");
            _prompt.IsHitTestVisible = false;
            _ = pc;
        }

        // ---- Bottom tray ---------------------------------------------------------------------------

        readonly TextBlock _handTitle = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 0, 0) };

        void BuildBottom()
        {
            _bottom.Children.Clear();
            Game g = _c.Game;
            if (g == null) return;

            if (g.PendingTrade != null && g.Phase == Phase.Main) _bottom.Children.Add(BuildTradeOffer(g));

            // The hand, fanned out on the left of the tray.
            Player viewer = _c.ViewerPlayer;
            _hand.Show(viewer.Hand, viewer.ActionCardCount, "Action");
            string dev = string.Join(", ", ((ActionCard[])Enum.GetValues(typeof(ActionCard)))
                .Where(c => viewer.ActionCardsTotal(c) > 0)
                .Select(c => $"{c} x{viewer.ActionCardsTotal(c)}" + (viewer.ActionCardsUsable(c) < viewer.ActionCardsTotal(c) ? " (new)" : "")));
            _handTitle.Inlines.Clear();
            _handTitle.Inlines.Add(new Avalonia.Controls.Documents.Run("YOUR HAND") { FontWeight = FontWeight.Black, Foreground = Palette.Brush(Color.FromRgb(0xff, 0xf3, 0xd6)) });
            _handTitle.Inlines.Add(new Avalonia.Controls.Documents.Run("   Action cards: " + (dev.Length == 0 ? "none" : dev)) { FontWeight = FontWeight.SemiBold, Foreground = Palette.Brush(Color.FromRgb(0xf6, 0xdc, 0xb0)) });
            _handTitle.MaxWidth = _hand.DesiredSize.Width > 0 ? Math.Max(_hand.DesiredSize.Width, 200) : 330;
            Detach(_handTitle);
            Detach(_hand);
            _handBox.Children.Add(_handTitle);
            _handBox.Children.Add(_hand);
            Detach(_handBox);

            // Actions for this moment of the turn.
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            void Add(Control c, double gap = 8)
            {
                c.Margin = new Thickness(0, 4, gap, 4);
                if (c is Button b)
                {
                    b.MinHeight = 50;
                    b.VerticalContentAlignment = VerticalAlignment.Center;
                }
                else c.VerticalAlignment = VerticalAlignment.Center;
                buttons.Children.Add(c);
            }

            Player me = g.Players[g.CurrentPlayer];
            // Online or while a computer player moves, the action buttons only appear on our own turn; otherwise the bar shows the prompt (the default case).
            bool waiting = g.Phase != Phase.GameOver && (_c.IsOnline ? g.CurrentPlayer != _c.MySeat : _c.IsBot(_c.Actor));
            switch (waiting ? Phase.Discard : g.Phase)
            {
                case Phase.Roll:
                    Add(Ui.Button("Roll dice", () => _c.Send(new RollDice(g.CurrentPlayer)), primary: true, minWidth: 160));
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
                    Add(BuildButton("City", PieceIcon.City, mine, Costs.City, Tool.City, me.CitiesLeft, "cities"), 16);
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
                    Add(Ui.Button("Play card", () => OpenModal(Modal.PlayCard)), 16);
                    Add(Ui.Button("End turn", () => _c.Send(new EndTurn(me.Id)), primary: true, minWidth: 130));
                    break;

                case Phase.GameOver:
                    if (_c.IsOnline) Add(Ui.Button("Leave game", LeaveOnline, primary: true, minWidth: 150));
                    else Add(Ui.Button("New game", () => OpenModal(Modal.Setup), primary: true, minWidth: 150));
                    break;

                default:
                    var prompt = Ui.Text(_c.Prompt(), 16, true, Colors.White);
                    prompt.FontWeight = FontWeight.Black;
                    prompt.Margin = new Thickness(4, 0, 0, 0);
                    Add(prompt);
                    break;
            }

            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            bar.Children.Add(_handBox);
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
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
                        Width = 10,
                        Height = 13,
                        CornerRadius = new CornerRadius(3),
                        Background = Palette.Brush(c),
                        BorderBrush = Palette.Brush(Palette.Ink),
                        BorderThickness = new Thickness(1.5),
                    });
                }
            }
            var label = new TextBlock { Text = name, FontWeight = FontWeight.Black, VerticalAlignment = VerticalAlignment.Center };
            var top = Ui.Row(7, icon.Draw(color, 20), label);
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
                Foreground = Palette.Brush(Palette.Text),
                FontWeight = FontWeight.Black,
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
            Border card = Ui.Panel(row, new Thickness(12, 6));
            card.HorizontalAlignment = HorizontalAlignment.Left;
            return card;
        }
    }
}
