using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// The game log on the right: compact lines, each with a small icon for what happened (rolls, building,
    /// steals, trades...), player names in their own colour and resource names in theirs. A new turn starts
    /// with a thin divider. The panel folds into a slim tab to give the board more room.
    /// </summary>
    public partial class MainWindow
    {
        void BuildLog()
        {
            _log.Children.Clear();
            _logHeader.Children.Clear();
            Game g = _c.Game;

            Button fold = Ui.Button(_logFolded ? "<" : ">", () =>
            {
                _logFolded = !_logFolded;
                Relayout(Bounds.Width > 0 ? Bounds.Width : Width);
                BuildLog();
            });
            fold.Classes.Add(GameTheme.Quiet);
            fold.Padding = new Thickness(0, 0, 0, 2);
            fold.Width = 30;
            fold.Height = 30;
            fold.CornerRadius = new CornerRadius(15);
            ToolTip.SetTip(fold, _logFolded ? "Show the game log" : "Fold the log away to give the board more room");

            _logScroll.IsVisible = !_logFolded;
            _chatRow.IsVisible = _c.IsOnline && !_logFolded;
            if (_logFolded)
            {
                _logHeader.Orientation = Orientation.Vertical;
                _logHeader.HorizontalAlignment = HorizontalAlignment.Center;
                _logHeader.Children.Add(fold);
                _logHeader.Children.Add(new Glyph(GlyphKind.Log, Palette.Parchment, 22) { HorizontalAlignment = HorizontalAlignment.Center });
                return;
            }

            _logHeader.Orientation = Orientation.Horizontal;
            _logHeader.HorizontalAlignment = HorizontalAlignment.Stretch;
            var title = Ui.Text(_c.IsOnline ? "Log & chat" : "Game log", 16, true, Palette.Text);
            title.FontWeight = FontWeight.Black;
            title.VerticalAlignment = VerticalAlignment.Center;
            var head = new DockPanel { Width = Math.Max(80, _right.Width - 34) };
            DockPanel.SetDock(fold, Dock.Right);
            head.Children.Add(fold);
            head.Children.Add(Ui.Row(6, new Glyph(GlyphKind.Log, Palette.Parchment, 18), title));
            _logHeader.Children.Add(head);

            int from = Math.Max(0, _c.Log.Count - 30);
            for (int i = from; i < _c.Log.Count; i++)
                _log.Children.Add(LogLine(g, _c.Log[i], i == _c.Log.Count - 1));
            Dispatcher.UIThread.Post(() => _logScroll.ScrollToEnd(), DispatcherPriority.Background);
        }

        /// <summary>What kind of news a log line is, read from its wording.</summary>
        static GlyphKind Classify(string line)
        {
            if (line.EndsWith("'s turn.", StringComparison.Ordinal)) return GlyphKind.Turn;
            if (line.Contains(" rolled ") || line.StartsWith("A 7 was rolled", StringComparison.Ordinal)) return GlyphKind.Dice;
            if (line.Contains(" wins!")) return GlyphKind.Trophy;
            if (line.Contains(" stole ") || line.Contains("raider") || line.Contains("Plunder") || line.Contains("earthquake") || line.Contains("plague") || line.Contains("tribute")) return GlyphKind.Steal;
            if (line.Contains("discard") || line.Contains(" lost ")) return GlyphKind.Discard;
            if (line.Contains(" trade") || line.Contains(" offers ") || line.Contains(" traded ")) return GlyphKind.Trade;
            if (line.Contains(" built ") || line.Contains(" placed ") || line.Contains("upgraded to a city")) return GlyphKind.Build;
            if (line.Contains("played ") || line.Contains("action card") || line.Contains(" drew ")) return GlyphKind.Action;
            if (line.Contains(" collected ") || line.Contains(" took ")) return GlyphKind.Collect;
            if (line.Contains("Great Road") || line.Contains("Grand Army")) return GlyphKind.Star;
            if (line.Contains(": ")) return GlyphKind.Chat;
            return GlyphKind.Info;
        }

        Control LogLine(Game g, string line, bool latest)
        {
            GlyphKind kind = Classify(line);
            if (kind == GlyphKind.Turn && g != null)
            {
                // A new turn: a slim divider with the player's name in a pill of their colour.
                Player p = g.Players.FirstOrDefault(x => line == $"{x.Name}'s turn.");
                if (p != null)
                {
                    Color pc = Palette.Player(p.Id);
                    var pill = new Border
                    {
                        Background = Palette.Brush(pc),
                        BorderBrush = Palette.Brush(Palette.Ink),
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(9),
                        Padding = new Thickness(8, 0, 8, 1),
                        Child = new TextBlock { Text = $"{p.Name}'s turn", FontSize = 11, FontWeight = FontWeight.Black, Foreground = Palette.Brush(Palette.OnPlayer(p.Id)) },
                    };
                    var rule = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 6, 0, 2) };
                    Border Line() => new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Palette.Brush(Palette.OutlineSoft), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
                    Border l = Line(), r = Line();
                    l.Margin = new Thickness(0, 0, 6, 0);
                    r.Margin = new Thickness(6, 0, 0, 0);
                    Grid.SetColumn(pill, 1);
                    Grid.SetColumn(r, 2);
                    rule.Children.Add(l);
                    rule.Children.Add(pill);
                    rule.Children.Add(r);
                    return rule;
                }
            }

            var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Color body = latest ? Palette.Text : Color.FromRgb(0x5e, 0x4a, 0x36);
            foreach (var (run, color, bold) in Tokens(g, line, body))
            {
                text.Inlines.Add(new Run(run)
                {
                    Foreground = Palette.Brush(color),
                    FontWeight = bold ? FontWeight.Black : (latest ? FontWeight.Bold : FontWeight.SemiBold),
                });
            }

            var icon = new Glyph(kind, Glyph.DefaultColor(kind), 14) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
            var row = new DockPanel { Margin = new Thickness(0, 1) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            if (!latest) return row;
            // The latest news gets a soft highlight.
            return new Border
            {
                Child = row,
                Background = Palette.Brush(Color.FromArgb(0x60, 0xff, 0xff, 0xff)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4, 2),
                Margin = new Thickness(-4, 1, 0, 0),
            };
        }

        static readonly string[] ResourceNames = ResourceSet.Types.Select(r => r.ToString()).ToArray();

        /// <summary>Splits a line into runs: player names in their colour, resource names in theirs, numbers bold.</summary>
        static IEnumerable<(string Text, Color Color, bool Bold)> Tokens(Game g, string line, Color body)
        {
            var marks = new List<(int At, int Length, Color Color)>();
            if (g != null)
            {
                foreach (Player p in g.Players.OrderByDescending(p => p.Name.Length))
                {
                    if (p.Name.Length == 0) continue;
                    for (int at = line.IndexOf(p.Name, StringComparison.Ordinal); at >= 0; at = line.IndexOf(p.Name, at + p.Name.Length, StringComparison.Ordinal))
                    {
                        if (!marks.Any(m => at < m.At + m.Length && m.At < at + p.Name.Length))
                            marks.Add((at, p.Name.Length, Palette.PlayerText(p.Id)));
                    }
                }
            }
            for (int i = 0; i < ResourceNames.Length; i++)
            {
                string word = ResourceNames[i];
                for (int at = line.IndexOf(word, StringComparison.Ordinal); at >= 0; at = line.IndexOf(word, at + word.Length, StringComparison.Ordinal))
                {
                    bool whole = (at == 0 || !char.IsLetter(line[at - 1])) && (at + word.Length == line.Length || !char.IsLetter(line[at + word.Length]));
                    if (whole && !marks.Any(m => at < m.At + m.Length && m.At < at + word.Length))
                        marks.Add((at, word.Length, Palette.ResourceText(ResourceSet.Types[i])));
                }
            }
            marks.Sort((a, b) => a.At.CompareTo(b.At));

            int pos = 0;
            foreach (var m in marks)
            {
                if (m.At > pos) yield return (line.Substring(pos, m.At - pos), body, false);
                yield return (line.Substring(m.At, m.Length), m.Color, true);
                pos = m.At + m.Length;
            }
            if (pos < line.Length) yield return (line.Substring(pos), body, false);
        }
    }
}
