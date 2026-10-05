using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>Tiny helpers for building the interface in code.</summary>
    public static class Ui
    {
        public static readonly Color Muted = Color.FromRgb(0x8c, 0x6e, 0x50);

        /// <summary>
        /// The interface font: a friendly rounded face where one is installed, falling back to sturdy system fonts
        /// (no font files ship with the game).
        /// </summary>
        public static readonly FontFamily Font = new FontFamily("Nunito, Fredoka, Baloo 2, Varela Round, Segoe UI, Trebuchet MS, Ubuntu, DejaVu Sans, Arial, sans-serif");

        public static TextBlock Text(string text, double size = 14, bool bold = false, Color? color = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = bold ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = Palette.Brush(color ?? Palette.Text),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        /// <summary>One line of text made of differently colored runs.</summary>
        public static TextBlock Colored(IEnumerable<(string Text, Color Color, bool Bold)> parts, double size = 14)
        {
            var block = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
            foreach (var (text, color, bold) in parts)
            {
                block.Inlines.Add(new Run(text)
                {
                    Foreground = Palette.Brush(color),
                    FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                });
            }
            return block;
        }

        public static Button Button(string text, Action onClick, bool enabled = true, bool primary = false, double minWidth = 0)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                IsEnabled = enabled,
                Padding = new Thickness(18, 8),
                MinWidth = minWidth,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (primary) button.Classes.Add(GameTheme.Primary);
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>A button that looks selected when <paramref name="selected"/> is true.</summary>
        public static Button Choice(string text, bool selected, Action onClick, bool enabled = true)
        {
            Button b = Button(text, onClick, enabled);
            if (selected) b.Classes.Add(GameTheme.Selected);
            return b;
        }

        public static StackPanel Row(double spacing = 8, params Control[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
            foreach (Control c in children) p.Children.Add(c);
            return p;
        }

        public static StackPanel Column(double spacing = 6, params Control[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
            foreach (Control c in children) p.Children.Add(c);
            return p;
        }

        /// <summary>A parchment panel with a thick wood-brown outline and a chunky drop shadow.</summary>
        public static Border Card(Control child, double width = 0)
        {
            var card = new Border
            {
                Background = Parchment(),
                BorderBrush = Palette.Brush(Palette.Outline),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(26, 22),
                BoxShadow = BoxShadows.Parse("0 7 0 0 #50274a6b, 0 22 44 0 #50173550"),
                Child = child,
            };
            if (width > 0) card.Width = width;
            return card;
        }

        public static IBrush Parchment() => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Palette.Parchment, 0), new GradientStop(Palette.ParchmentDeep, 1) },
        };

        /// <summary>A smaller parchment panel for the game screen's side columns.</summary>
        public static Border Panel(Control child, Thickness? padding = null) => new Border
        {
            Background = Parchment(),
            BorderBrush = Palette.Brush(Palette.Outline),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(18),
            Padding = padding ?? new Thickness(12, 10),
            BoxShadow = BoxShadows.Parse("0 5 0 0 #40204060"),
            Child = child,
        };

        /// <summary>A dialog title: big chunky text over a short, rounded orange rule.</summary>
        public static Control Heading(string text, double size = 26)
        {
            var title = Text(text, size, true, Palette.Text);
            title.FontWeight = FontWeight.Black;
            var rule = new Border
            {
                Height = 6,
                Width = 56,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Palette.Brush(Palette.Orange),
                Margin = new Thickness(0, 6, 0, 2),
            };
            return Column(0, title, rule);
        }

        /// <summary>A small capitalised label that heads a group of controls.</summary>
        public static TextBlock Section(string text)
        {
            var t = Text(text.ToUpperInvariant(), 11, true, Muted);
            t.FontWeight = FontWeight.Black;
            t.LetterSpacing = 1.2;
            t.Margin = new Thickness(0, 6, 0, 0);
            return t;
        }

        /// <summary>A small rounded label, for points, turn numbers and badges.</summary>
        public static Border Chip(string text, Color fg, Color bg, double size = 12) => new Border
        {
            Background = Palette.Brush(bg),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 1, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.Black, Foreground = Palette.Brush(fg) },
        };

        public static Control Dot(Color color, double size = 12) => new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = Palette.Brush(color),
            Stroke = Palette.Brush(Palette.Ink),
            StrokeThickness = Math.Max(1.5, size / 7),
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Label with - / + buttons. The value text updates itself; <paramref name="onChange"/> gets the new value.</summary>
        public static Control Stepper(string label, int value, int min, int max, Action<int> onChange, double labelWidth = 220, Color? labelColor = null)
        {
            var valueText = Text(value.ToString(), 16, true);
            valueText.FontWeight = FontWeight.Black;
            valueText.MinWidth = 34;
            valueText.TextAlignment = TextAlignment.Center;
            valueText.VerticalAlignment = VerticalAlignment.Center;

            int current = value;
            Button minus = null, plus = null;

            void Sync()
            {
                valueText.Text = current.ToString();
                minus.IsEnabled = current > min;
                plus.IsEnabled = current < max;
            }

            void Step(int delta)
            {
                int next = Math.Clamp(current + delta, min, max);
                if (next == current) return;
                current = next;
                Sync();
                onChange(current);
            }

            minus = RoundButton("-", () => Step(-1));
            plus = RoundButton("+", () => Step(+1));
            Sync();

            var lab = labelColor.HasValue ? Text(label, 14, true, labelColor.Value) : Text(label, 14, true);
            lab.Width = labelWidth;
            lab.VerticalAlignment = VerticalAlignment.Center;
            return Row(8, lab, minus, valueText, plus);
        }

        /// <summary>A small round candy button, for steppers.</summary>
        static Button RoundButton(string text, Action onClick)
        {
            Button b = Button(text, onClick);
            b.Classes.Add(GameTheme.Round);
            b.Width = 38;
            b.Height = 38;
            b.Padding = new Thickness(0, 0, 0, 2);
            b.VerticalContentAlignment = VerticalAlignment.Center;
            return b;
        }

        public static string ResourceCounts(ResourceSet s) => s.Describe();
    }
}
