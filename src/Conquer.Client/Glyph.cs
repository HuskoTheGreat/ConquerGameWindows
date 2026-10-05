using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Conquer.Client
{
    /// <summary>The little pictures used in the log and on the player cards.</summary>
    public enum GlyphKind { Cards, Action, Soldier, Road, Dice, Build, Steal, Trade, Collect, Trophy, Info, Chat, Star, Log, Turn, Discard }

    /// <summary>A tiny cartoon icon: a coloured shape with a dark ink outline, drawn in code at any size.</summary>
    public sealed class Glyph : Control
    {
        public GlyphKind Kind { get; }
        public Color Color { get; }

        public Glyph(GlyphKind kind, Color color, double size = 16)
        {
            Kind = kind;
            Color = color;
            Width = size;
            Height = size;
            VerticalAlignment = VerticalAlignment.Center;
        }

        /// <summary>The usual colour of each kind of icon.</summary>
        public static Color DefaultColor(GlyphKind kind) => kind switch
        {
            GlyphKind.Dice => Color.FromRgb(0xff, 0xff, 0xff),
            GlyphKind.Build => Color.FromRgb(0x5c, 0xc0, 0x4a),
            GlyphKind.Steal => Color.FromRgb(0xe8, 0x4a, 0x3c),
            GlyphKind.Discard => Color.FromRgb(0xe8, 0x4a, 0x3c),
            GlyphKind.Trade => Color.FromRgb(0xa8, 0x6c, 0xe8),
            GlyphKind.Collect => Color.FromRgb(0xff, 0xc6, 0x2e),
            GlyphKind.Trophy => Color.FromRgb(0xff, 0xc6, 0x2e),
            GlyphKind.Star => Color.FromRgb(0xff, 0xc6, 0x2e),
            GlyphKind.Action => Color.FromRgb(0x8a, 0x6c, 0xe0),
            GlyphKind.Soldier => Color.FromRgb(0x4a, 0x8c, 0xe8),
            GlyphKind.Road => Color.FromRgb(0xc9, 0x8a, 0x4b),
            GlyphKind.Chat => Color.FromRgb(0x4a, 0xb8, 0xe8),
            GlyphKind.Cards => Color.FromRgb(0xff, 0xf3, 0xd6),
            _ => Color.FromRgb(0xb8, 0xa4, 0x8a),
        };

        public override void Render(DrawingContext ctx) =>
            Draw(ctx, Kind, new Point(Bounds.Width / 2, Bounds.Height / 2), Math.Min(Bounds.Width, Bounds.Height), Color);

        public static void Draw(DrawingContext ctx, GlyphKind kind, Point c, double size, Color color)
        {
            double u = size / 2;
            IBrush fill = Palette.Brush(color);
            var ink = new Pen(Palette.Brush(Palette.Ink), Math.Max(1.2, size * 0.09), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            IBrush inkFill = Palette.Brush(Palette.Ink);
            switch (kind)
            {
                case GlyphKind.Cards:
                    ctx.DrawRectangle(Palette.Brush(Palette.Darken(color, 0.85)), ink, new Rect(c.X - u * 0.75, c.Y - u * 0.8, u * 1.0, u * 1.4), u * 0.18, u * 0.18);
                    ctx.DrawRectangle(fill, ink, new Rect(c.X - u * 0.25, c.Y - u * 0.6, u * 1.0, u * 1.4), u * 0.18, u * 0.18);
                    break;
                case GlyphKind.Action:
                    ctx.DrawRectangle(fill, ink, new Rect(c.X - u * 0.62, c.Y - u * 0.85, u * 1.24, u * 1.7), u * 0.2, u * 0.2);
                    ctx.DrawGeometry(Palette.Brush(Palette.Gold), null, Star(new Point(c.X, c.Y), u * 0.45));
                    break;
                case GlyphKind.Soldier:
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.75, c.Y - u * 0.75), new Point(c.X + u * 0.75, c.Y - u * 0.75),
                        new Point(c.X + u * 0.7, c.Y + u * 0.1), new Point(c.X, c.Y + u * 0.9), new Point(c.X - u * 0.7, c.Y + u * 0.1)));
                    ctx.DrawLine(new Pen(Palette.Brush(Colors.White), Math.Max(1, size * 0.1), lineCap: PenLineCap.Round), new Point(c.X, c.Y - u * 0.45), new Point(c.X, c.Y + u * 0.45));
                    break;
                case GlyphKind.Road:
                    var plank = Poly(new Point(c.X - u * 0.9, c.Y + u * 0.45), new Point(c.X + u * 0.45, c.Y - u * 0.9), new Point(c.X + u * 0.9, c.Y - u * 0.45), new Point(c.X - u * 0.45, c.Y + u * 0.9));
                    ctx.DrawGeometry(fill, ink, plank);
                    break;
                case GlyphKind.Dice:
                    ctx.DrawRectangle(fill, ink, new Rect(c.X - u * 0.8, c.Y - u * 0.8, u * 1.6, u * 1.6), u * 0.35, u * 0.35);
                    foreach (var (dx, dy) in new[] { (-0.38, -0.38), (0.0, 0.0), (0.38, 0.38) })
                        ctx.DrawEllipse(inkFill, null, new Point(c.X + dx * u, c.Y + dy * u), u * 0.15, u * 0.15);
                    break;
                case GlyphKind.Build:
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.75, c.Y + u * 0.8), new Point(c.X - u * 0.75, c.Y - u * 0.1), new Point(c.X, c.Y - u * 0.85),
                        new Point(c.X + u * 0.75, c.Y - u * 0.1), new Point(c.X + u * 0.75, c.Y + u * 0.8)));
                    ctx.DrawRectangle(inkFill, null, new Rect(c.X - u * 0.18, c.Y + u * 0.2, u * 0.36, u * 0.6));
                    break;
                case GlyphKind.Steal:
                    // A bandit's mask.
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.95, c.Y - u * 0.35), new Point(c.X + u * 0.95, c.Y - u * 0.35), new Point(c.X + u * 0.8, c.Y + u * 0.35),
                        new Point(c.X + u * 0.15, c.Y + u * 0.45), new Point(c.X, c.Y + u * 0.2), new Point(c.X - u * 0.15, c.Y + u * 0.45), new Point(c.X - u * 0.8, c.Y + u * 0.35)));
                    ctx.DrawEllipse(Palette.Brush(Colors.White), null, new Point(c.X - u * 0.42, c.Y), u * 0.2, u * 0.14);
                    ctx.DrawEllipse(Palette.Brush(Colors.White), null, new Point(c.X + u * 0.42, c.Y), u * 0.2, u * 0.14);
                    break;
                case GlyphKind.Discard:
                    ctx.DrawRectangle(fill, ink, new Rect(c.X - u * 0.6, c.Y - u * 0.8, u * 1.2, u * 1.6), u * 0.2, u * 0.2);
                    var x = new Pen(Palette.Brush(Colors.White), Math.Max(1.2, size * 0.12), lineCap: PenLineCap.Round);
                    ctx.DrawLine(x, new Point(c.X - u * 0.28, c.Y - u * 0.3), new Point(c.X + u * 0.28, c.Y + u * 0.3));
                    ctx.DrawLine(x, new Point(c.X + u * 0.28, c.Y - u * 0.3), new Point(c.X - u * 0.28, c.Y + u * 0.3));
                    break;
                case GlyphKind.Trade:
                    var arrow = new Pen(fill, Math.Max(1.5, size * 0.18), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    var arrowInk = new Pen(Palette.Brush(Palette.Ink), Math.Max(2.5, size * 0.32), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    foreach (Pen p in new[] { arrowInk, arrow })
                    {
                        ctx.DrawGeometry(null, p, Path(new Point(c.X - u * 0.75, c.Y - u * 0.3), new Point(c.X + u * 0.75, c.Y - u * 0.3), new Point(c.X + u * 0.4, c.Y - u * 0.65)));
                        ctx.DrawGeometry(null, p, Path(new Point(c.X + u * 0.75, c.Y + u * 0.35), new Point(c.X - u * 0.75, c.Y + u * 0.35), new Point(c.X - u * 0.4, c.Y + u * 0.7)));
                    }
                    break;
                case GlyphKind.Collect:
                    ctx.DrawEllipse(fill, ink, new Point(c.X, c.Y + u * 0.15), u * 0.8, u * 0.7);
                    ctx.DrawLine(new Pen(Palette.Brush(Palette.Ink), Math.Max(1.2, size * 0.1), lineCap: PenLineCap.Round), new Point(c.X - u * 0.35, c.Y - u * 0.62), new Point(c.X + u * 0.35, c.Y - u * 0.62));
                    ctx.DrawEllipse(Palette.Brush(Colors.White), null, new Point(c.X - u * 0.3, c.Y - u * 0.05), u * 0.16, u * 0.1);
                    break;
                case GlyphKind.Trophy:
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.7, c.Y - u * 0.85), new Point(c.X + u * 0.7, c.Y - u * 0.85), new Point(c.X + u * 0.5, c.Y - u * 0.05),
                        new Point(c.X + u * 0.15, c.Y + u * 0.2), new Point(c.X + u * 0.15, c.Y + u * 0.5), new Point(c.X + u * 0.5, c.Y + u * 0.85),
                        new Point(c.X - u * 0.5, c.Y + u * 0.85), new Point(c.X - u * 0.15, c.Y + u * 0.5), new Point(c.X - u * 0.15, c.Y + u * 0.2), new Point(c.X - u * 0.5, c.Y - u * 0.05)));
                    break;
                case GlyphKind.Star:
                    ctx.DrawGeometry(fill, ink, Star(c, u * 0.95));
                    break;
                case GlyphKind.Chat:
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.85, c.Y - u * 0.7), new Point(c.X + u * 0.85, c.Y - u * 0.7), new Point(c.X + u * 0.85, c.Y + u * 0.35),
                        new Point(c.X - u * 0.1, c.Y + u * 0.35), new Point(c.X - u * 0.55, c.Y + u * 0.85), new Point(c.X - u * 0.45, c.Y + u * 0.35), new Point(c.X - u * 0.85, c.Y + u * 0.35)));
                    break;
                case GlyphKind.Log:
                    ctx.DrawRectangle(Palette.Brush(Palette.Parchment), ink, new Rect(c.X - u * 0.7, c.Y - u * 0.85, u * 1.4, u * 1.7), u * 0.2, u * 0.2);
                    var line = new Pen(Palette.Brush(Palette.Ink), Math.Max(1, size * 0.08), lineCap: PenLineCap.Round);
                    for (int i = 0; i < 3; i++) ctx.DrawLine(line, new Point(c.X - u * 0.35, c.Y - u * 0.4 + i * u * 0.4), new Point(c.X + u * 0.35, c.Y - u * 0.4 + i * u * 0.4));
                    break;
                case GlyphKind.Turn:
                    ctx.DrawGeometry(fill, ink, Poly(new Point(c.X - u * 0.6, c.Y - u * 0.75), new Point(c.X + u * 0.8, c.Y), new Point(c.X - u * 0.6, c.Y + u * 0.75)));
                    break;
                default:
                    ctx.DrawEllipse(fill, ink, c, u * 0.8, u * 0.8);
                    ctx.DrawEllipse(inkFill, null, new Point(c.X, c.Y - u * 0.35), u * 0.12, u * 0.12);
                    ctx.DrawLine(new Pen(inkFill, Math.Max(1, size * 0.12), lineCap: PenLineCap.Round), new Point(c.X, c.Y - u * 0.05), new Point(c.X, c.Y + u * 0.42));
                    break;
            }
        }

        public static Geometry Star(Point c, double r)
        {
            var pts = new Point[10];
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 5 * i - Math.PI / 2, k = i % 2 == 0 ? r : r * 0.46;
                pts[i] = new Point(c.X + Math.Cos(a) * k, c.Y + Math.Sin(a) * k);
            }
            return Poly(pts);
        }

        static Geometry Poly(params Point[] pts)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                g.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
                g.EndFigure(true);
            }
            return geo;
        }

        static Geometry Path(params Point[] pts)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                g.BeginFigure(pts[0], false);
                for (int i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
                g.EndFigure(false);
            }
            return geo;
        }
    }
}
