using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// Board setup: before a game (single player, or as host of a room) the board can be arranged tile by tile,
    /// shuffled, or rerolled. The arranged board is kept until the board size changes or a random one is chosen.
    /// </summary>
    public partial class MainWindow
    {
        readonly LocalGameController _previewC = new LocalGameController();
        readonly BoardControl _preview = new BoardControl { QuietSpots = true, Name = "BoardPreview" };
        readonly Random _shuffles = new Random();

        /// <summary>The board the next game starts on, or null for a random one.</summary>
        Board _customBoard;

        BoardDraft _arrange;
        Hex? _picked;
        bool _swapMode;
        Modal _boardReturn = Modal.Setup;

        /// <summary>The board chosen on the board setup screen, or null for a random one (exposed for tests).</summary>
        public Board CustomBoard => _customBoard;

        /// <summary>The draft open on the board setup screen (exposed for tests).</summary>
        public BoardDraft ArrangingBoard => _arrange;

        void SetUpBoardPreview()
        {
            _preview.Controller = _previewC;
            _previewC.TileClicked += OnPreviewTile;
        }

        /// <summary>Opens the board setup screen for a board of <paramref name="radius"/>, starting from the arranged board if there is one.</summary>
        public void OpenBoardSetup(int radius)
        {
            _boardReturn = _modal == Modal.BoardSetup ? _boardReturn : _modal;
            _arrange = _customBoard != null && _customBoard.Radius == radius
                ? BoardDraft.From(_customBoard)
                : BoardDraft.Random(radius, _shuffles.Next());
            _picked = null;
            _swapMode = false;
            ShowDraft();
            OpenModal(Modal.BoardSetup);
        }

        /// <summary>Keeps the arranged board and goes back to where the screen was opened from.</summary>
        public void UseDraftBoard()
        {
            try
            {
                _customBoard = _arrange.Build();
            }
            catch (ArgumentException e)
            {
                ShowToast("That board can't be played: " + e.Message);
                return;
            }
            LeaveBoardSetup();
        }

        void LeaveBoardSetup()
        {
            _arrange = null;
            _picked = null;
            OpenModal(_boardReturn);
        }

        /// <summary>The board size changed, so an arranged board no longer fits.</summary>
        void ForgetBoardIfNot(int radius)
        {
            if (_customBoard != null && _customBoard.Radius != radius) _customBoard = null;
        }

        void ShowDraft()
        {
            Board board;
            try
            {
                board = _arrange.Build();
            }
            catch (ArgumentException)
            {
                return; // can't happen through this screen; keep showing the last good one
            }
            _previewC.ShowBoard(board);
            _preview.Marked = _picked;
        }

        void OnPreviewTile(Hex hex)
        {
            if (_arrange == null || !_arrange.Contains(hex)) return;
            if (_swapMode && _picked.HasValue && _picked.Value != hex)
            {
                _arrange.Swap(_picked.Value, hex);
                _picked = null;
            }
            else
            {
                _picked = _picked == hex ? null : hex;
            }
            ShowDraft();
            BuildOverlay();
        }

        void ChangeDraft(Action<BoardDraft> change)
        {
            change(_arrange);
            ShowDraft();
            BuildOverlay();
        }

        // ---- The screen ----------------------------------------------------------------------------

        Control BuildBoardSetup()
        {
            BoardDraft d = _arrange;
            if (d == null) return null;

            // The preview is one control reused across rebuilds, so take it out of the last card first.
            if (_preview.Parent is Border old) old.Child = null;
            double boardHeight = Math.Clamp(ClientSize.Height - 200, 360, 640);
            var boardHost = new Border
            {
                Child = _preview,
                Width = Math.Clamp(ClientSize.Width - 520, 420, 760),
                Height = boardHeight,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
            };

            var side = Ui.Column(8,
                Ui.Text("Arrange the board", 24, true),
                Ui.Text(_swapMode
                    ? "Swap mode: click a tile, then the tile to trade places with."
                    : "Click a tile to change what it is and its number.", 13, false, Ui.Muted));

            var swap = new CheckBox { Content = "Swap tiles (click two tiles)", IsChecked = _swapMode, Foreground = Palette.Brush(Colors.White) };
            swap.IsCheckedChanged += (_, _) =>
            {
                _swapMode = swap.IsChecked == true;
                BuildOverlay();
            };
            side.Children.Add(swap);

            if (_picked.HasValue && !_swapMode) side.Children.Add(BuildTileEditor(d, _picked.Value));

            side.Children.Add(new Border { Height = 4 });
            side.Children.Add(Ui.Text("Whole board", 15, true));
            var tools = new WrapPanel { Orientation = Orientation.Horizontal };
            void Tool(string text, Action<BoardDraft> change)
            {
                Button b = Ui.Button(text, () => ChangeDraft(change));
                b.Margin = new Thickness(0, 0, 6, 6);
                tools.Children.Add(b);
            }
            Tool("Shuffle tiles", x => x.ShuffleTiles(_shuffles.Next()));
            Tool("Shuffle numbers", x => x.ShuffleNumbers(_shuffles.Next()));
            Tool("New harbors", x => x.ShuffleHarbors(_shuffles.Next()));
            Button fresh = Ui.Button("New random board", () =>
            {
                _arrange = BoardDraft.Random(d.Radius, _shuffles.Next());
                _picked = null;
                ShowDraft();
                BuildOverlay();
            });
            fresh.Margin = new Thickness(0, 0, 6, 6);
            tools.Children.Add(fresh);
            side.Children.Add(tools);

            string counts = string.Join("   ", BoardDraft.Kinds.Select(k => $"{KindName(k)} {d.Count(k)}"));
            side.Children.Add(Ui.Text($"{d.Hexes.Count} tiles: {counts}", 12, false, Ui.Muted));
            foreach (string note in d.Notes())
                side.Children.Add(Ui.Text(note, 12, false, Color.FromRgb(0xff, 0xd0, 0x8a)));

            side.Children.Add(new Border { Height = 6 });
            side.Children.Add(Ui.Row(8,
                Ui.Button("Use this board", UseDraftBoard, primary: true, minWidth: 150),
                Ui.Button("Cancel", LeaveBoardSetup)));

            var sideScroll = new ScrollViewer { Content = side, MaxHeight = boardHeight, Width = 380 };
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto") };
            Grid.SetColumn(sideScroll, 2);
            layout.Children.Add(boardHost);
            layout.Children.Add(sideScroll);
            return Ui.Card(layout);
        }

        Control BuildTileEditor(BoardDraft d, Hex hex)
        {
            Resource kind = d.ResourceAt(hex);
            var col = Ui.Column(6, Ui.Text("Selected tile", 15, true));

            var kinds = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (Resource k in BoardDraft.Kinds)
            {
                Resource pick = k;
                Button b = Ui.Choice(KindName(k), kind == k, () => ChangeDraft(x => x.SetResource(hex, pick)));
                b.Margin = new Thickness(0, 0, 6, 6);
                if (kind != k) b.BorderBrush = Palette.Brush(Palette.Resource(k));
                if (kind != k) b.BorderThickness = new Thickness(0, 0, 0, 3);
                kinds.Children.Add(b);
            }
            col.Children.Add(kinds);

            if (kind != Resource.Wasteland)
            {
                var numbers = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (int n in BoardDraft.Numbers)
                {
                    int pick = n;
                    Button b = Ui.Choice(n.ToString(), d.NumberAt(hex) == n, () => ChangeDraft(x => x.SetNumber(hex, pick)));
                    b.MinWidth = 40;
                    b.Margin = new Thickness(0, 0, 4, 4);
                    if (n == 6 || n == 8) b.Foreground = Palette.Brush(Color.FromRgb(0xff, 0x8a, 0x80));
                    numbers.Children.Add(b);
                }
                col.Children.Add(Ui.Text("Number", 13, false, Ui.Muted));
                col.Children.Add(numbers);
            }
            return new Border
            {
                Background = Palette.Brush(Color.FromRgb(0x2e, 0x34, 0x40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = col,
            };
        }

        static string KindName(Resource r) => r == Resource.Wasteland ? "Wasteland" : r.ToString();

        /// <summary>The "Board" line on the new game screen and in the lobby: random or arranged, with a button to arrange it.</summary>
        Control BoardChoiceRow(int radius)
        {
            ForgetBoardIfNot(radius);
            var label = Ui.Text("Board", 14);
            label.Width = 212;
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = Ui.Row(8, label,
                Ui.Text(_customBoard != null ? "Arranged" : "Random", 14, true, _customBoard != null ? Palette.Highlight : (Color?)null),
                Ui.Button(_customBoard != null ? "Edit board" : "Arrange board", () => OpenBoardSetup(radius)));
            row.Children[1].VerticalAlignment = VerticalAlignment.Center;
            if (_customBoard != null)
                row.Children.Add(Ui.Button("Use random", () =>
                {
                    _customBoard = null;
                    BuildOverlay();
                }));
            return row;
        }
    }
}
