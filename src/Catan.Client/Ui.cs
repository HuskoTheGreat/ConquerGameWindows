using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Catan.Core;

namespace Catan.Client
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
            if (primary) button.Background = Palette.Brush(Color.FromRgb(0x2f, 0x6f, 0xd8));
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>A button that looks selected when <paramref name="selected"/> is true.</summary>
        public static Button Choice(string text, bool selected, Action onClick, bool enabled = true)
        {
            Button b = Button(text, onClick, enabled);
            if (selected)
            {
                b.Background = Palette.Brush(Palette.Highlight);
                b.Foreground = Palette.Brush(Colors.Black);
                if (b.Content is TextBlock t) t.Foreground = Palette.Brush(Colors.Black);
            }
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
                Background = Palette.Brush(Palette.Panel),
                BorderBrush = Palette.Brush(Palette.PanelEdge),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Child = child,
            };
            if (width > 0) card.Width = width;
            return card;
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
        public static Control Stepper(string label, int value, int min, int max, Action<int> onChange, double labelWidth = 220)
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

            var lab = Text(label, 14);
            lab.Width = labelWidth;
            lab.VerticalAlignment = VerticalAlignment.Center;
            return Row(8, lab, minus, valueText, plus);
        }

        public static string ResourceCounts(ResourceSet s) => s.Describe();

        public static TextBlock ResourceLine(ResourceSet s, double size = 14)
        {
            var parts = new List<(string, Color, bool)>();
            foreach (Resource r in ResourceSet.Types)
            {
                if (parts.Count > 0) parts.Add(("   ", Colors.White, false));
                parts.Add(($"{r} ", Palette.Resource(r), true));
                parts.Add((s[r].ToString(), Colors.White, true));
            }
            return Colored(parts, size);
        }
    }
}
