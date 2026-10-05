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
            Color wc = Palette.Player(winner.Id);
            var title = Ui.Text($"{winner.Name} wins!", 28, true, Palette.Text);
            title.VerticalAlignment = VerticalAlignment.Center;
            var victory = Ui.Text("VICTORY", 12, true, Palette.Gold);
            victory.LetterSpacing = 3;

            var col = Ui.Column(8, victory, Ui.Row(12, Ui.Dot(wc, 22), title), new Border { Height = 4 });

            int rank = 0;
            foreach (Player p in g.Players.OrderByDescending(p => g.VictoryPoints(p.Id)).ThenBy(p => p.Id))
            {
                rank++;
                bool top = p.Id == g.Winner;
                var place = Ui.Text(rank.ToString(), 13, true, top ? Palette.Gold : Ui.Muted);
                place.Width = 18;
                var who = Ui.Text(p.Name, 14, top, Palette.Text);
                var points = Ui.Text($"{g.VictoryPoints(p.Id)} VP", 14, true, top ? Palette.Gold : Ui.Muted);
                var row = new DockPanel();
                DockPanel.SetDock(points, Dock.Right);
                row.Children.Add(points);
                row.Children.Add(Ui.Row(8, place, Ui.Dot(Palette.Player(p.Id)), who));
                col.Children.Add(new Border
                {
                    Child = row,
                    Padding = new Thickness(10, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = Palette.Brush(top ? Color.FromArgb(0x24, Palette.Gold.R, Palette.Gold.G, Palette.Gold.B) : Palette.SideRaised),
                });
            }

            col.Children.Add(new Border { Height = 4 });
            var buttons = Ui.Row(8);
            if (!_c.IsOnline) buttons.Children.Add(Ui.Button("Play again", () => OpenModal(Modal.Setup), primary: true, minWidth: 140));
            Button view = Ui.Button("View board", () =>
            {
                _resultsHiddenFor = _c.GameNumber;
                BuildResults();
            });
            view.Classes.Add(GameTheme.Quiet);
            buttons.Children.Add(view);
            col.Children.Add(buttons);

            Border card = Ui.Card(col, 380);
            card.BorderBrush = Palette.Brush(wc);
            card.BorderThickness = new Thickness(0, 4, 0, 0);
            _results.Child = card;
            _results.HorizontalAlignment = HorizontalAlignment.Center;
            _results.VerticalAlignment = VerticalAlignment.Bottom;
        }
    }
}
