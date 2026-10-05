using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// When a game ends, a results card rises over the board: the winner and the final standings. It doesn't
    /// block anything, and it can be put away to look over the final board.
    /// </summary>
    public partial class MainWindow
    {
        readonly Border _results = new Border { Name = "Results", IsVisible = false, Margin = new Thickness(0, 0, 0, 28) };
        int _resultsHiddenFor = -1;

        void BuildResults()
        {
            Game g = _c.Game;
            bool show = g != null && g.Phase == Phase.GameOver && g.Winner >= 0 && _resultsHiddenFor != _c.GameNumber;
            _results.IsVisible = show;
            if (!show)
            {
                _results.Child = null;
                return;
            }

            Player winner = g.Players[g.Winner];
            var title = Ui.Text($"{winner.Name} wins!", 30, true, Palette.PlayerText(winner.Id));
            title.FontWeight = FontWeight.Black;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextWrapping = TextWrapping.NoWrap;

            // A blue ribbon with a trophy heads the card.
            var ribbonText = new TextBlock { Text = "VICTORY", FontSize = 15, FontWeight = FontWeight.Black, LetterSpacing = 3, Foreground = Palette.Brush(Colors.White), VerticalAlignment = VerticalAlignment.Center };
            var ribbon = new Border
            {
                Child = Ui.Row(8, new Glyph(GlyphKind.Trophy, Palette.Gold, 22), ribbonText, new Glyph(GlyphKind.Trophy, Palette.Gold, 22)),
                Background = Palette.Brush(Palette.SelectDeep),
                BorderBrush = Palette.Brush(Color.FromRgb(0x10, 0x3a, 0x6c)),
                BorderThickness = new Thickness(3, 3, 3, 5),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 4, 14, 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -40, 0, 0),
            };

            var head = Ui.Row(12, Avatar(winner, 44), title);
            head.HorizontalAlignment = HorizontalAlignment.Center;
            var col = Ui.Column(8, ribbon, head, new Border { Height = 2 });

            int rank = 0;
            foreach (Player p in g.Players.OrderByDescending(p => g.VictoryPoints(p.Id)).ThenBy(p => p.Id))
            {
                rank++;
                bool top = p.Id == g.Winner;
                var place = Ui.Text(rank.ToString(), 15, true, top ? Palette.GoldDeep : Ui.Muted);
                place.FontWeight = FontWeight.Black;
                place.Width = 18;
                place.VerticalAlignment = VerticalAlignment.Center;
                var who = Ui.Text(p.Name, 15, true, Palette.Text);
                who.VerticalAlignment = VerticalAlignment.Center;
                if (top) who.FontWeight = FontWeight.Black;
                var points = Ui.Row(3, new Glyph(GlyphKind.Star, Palette.Gold, 16), Ui.Text($"{g.VictoryPoints(p.Id)} VP", 15, true, Palette.Text));
                ((TextBlock)points.Children[1]).FontWeight = FontWeight.Black;
                var row = new DockPanel();
                DockPanel.SetDock(points, Dock.Right);
                row.Children.Add(points);
                row.Children.Add(Ui.Row(8, place, Avatar(p, 24), who));
                col.Children.Add(new Border
                {
                    Child = row,
                    Padding = new Thickness(10, 5),
                    CornerRadius = new CornerRadius(14),
                    Background = Palette.Brush(top ? Palette.Lighten(Palette.Gold, 0.55) : Palette.SideRaised),
                    BorderBrush = Palette.Brush(top ? Palette.GoldDeep : Palette.OutlineSoft),
                    BorderThickness = new Thickness(2),
                });
            }

            col.Children.Add(new Border { Height = 4 });
            var buttons = Ui.Row(8);
            buttons.HorizontalAlignment = HorizontalAlignment.Center;
            if (!_c.IsOnline) buttons.Children.Add(Ui.Button("Play again", () => OpenModal(Modal.Setup), primary: true, minWidth: 150));
            Button view = Ui.Button("View board", () =>
            {
                _resultsHiddenFor = _c.GameNumber;
                BuildResults();
            });
            view.Classes.Add(GameTheme.Quiet);
            buttons.Children.Add(view);
            col.Children.Add(buttons);

            Border card = Ui.Card(col, 400);
            card.Padding = new Thickness(24, 14, 24, 20);
            card.Margin = new Thickness(0, 30, 0, 0);
            _results.Child = card;
            _results.HorizontalAlignment = HorizontalAlignment.Center;
            _results.VerticalAlignment = VerticalAlignment.Bottom;
        }
    }
}
