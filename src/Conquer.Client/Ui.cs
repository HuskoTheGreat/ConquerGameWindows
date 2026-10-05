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
        public static readonly Color Muted = Color.FromRgb(0xa4, 0xac, 0xbc);

        public static TextBlock Text(string text, double size = 14, bool bold = false, Color? color = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Palette.Brush(color ?? Colors.White),
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
                Padding = new Thickness(14, 8),
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

        public static Border Card(Control child, double width = 0)
        {
            var card = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Palette.PanelTop, 0), new GradientStop(Palette.Panel, 0.35), new GradientStop(Palette.Darken(Palette.Panel, 0.86), 1) },
                },
                BorderBrush = Palette.Brush(Palette.PanelEdge),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(24, 20),
                BoxShadow = BoxShadows.Parse("0 18 48 0 #A0000000"),
                Child = child,
            };
            if (width > 0) card.Width = width;
            return card;
        }

        /// <summary>A dialog title: large bold text over a short gold rule.</summary>
        public static Control Heading(string text, double size = 24)
        {
            var title = Text(text, size, true, Palette.Text);
            var rule = new Border
            {
                Height = 3,
                Width = 44,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Palette.Brush(Palette.Gold),
                Margin = new Thickness(0, 6, 0, 2),
            };
            return Column(0, title, rule);
        }

        /// <summary>A small capitalised label that heads a group of controls.</summary>
        public static TextBlock Section(string text)
        {
            var t = Text(text.ToUpperInvariant(), 11, true, Muted);
            t.LetterSpacing = 1.2;
            t.Margin = new Thickness(0, 6, 0, 0);
            return t;
        }

        public static Control Dot(Color color, double size = 12) => new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = Palette.Brush(color),
            Stroke = Palette.Brush(Colors.Black),
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Label with - / + buttons. The value text updates itself; <paramref name="onChange"/> gets the new value.</summary>
        public static Control Stepper(string label, int value, int min, int max, Action<int> onChange, double labelWidth = 220, Color? labelColor = null)
        {
            var valueText = Text(value.ToString(), 15, true);
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

            minus = Button("-", () => Step(-1), minWidth: 36);
            plus = Button("+", () => Step(+1), minWidth: 36);
            Sync();

            var lab = labelColor.HasValue ? Text(label, 14, true, labelColor.Value) : Text(label, 14);
            lab.Width = labelWidth;
            lab.VerticalAlignment = VerticalAlignment.Center;
            return Row(8, lab, minus, valueText, plus);
        }

        public static string ResourceCounts(ResourceSet s) => s.Describe();
    }
}
