using System;
using Avalonia;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client.Animation
{
    /// <summary>Draws resource and action cards with simple vector art, at any size and orientation.</summary>
    public static class CardArt
    {
        public const double Aspect = 1.4; // height / width

        static readonly Color Back = Color.FromRgb(0x7a, 0x2e, 0x24);
        static readonly Color BackInk = Color.FromRgb(0xe9, 0xc4, 0x6a);
        static readonly Color DevFace = Color.FromRgb(0x4a, 0x3a, 0x7a);

        static readonly Typeface Bold = new Typeface(Ui.Font, FontStyle.Normal, FontWeight.Black);

        /// <summary>
        /// Draws a card centered on <paramref name="center"/>. <paramref name="flip"/> squashes it horizontally
        /// (1 = flat on, 0 = edge on) to fake a 3D turn; <paramref name="faceUp"/> picks which side shows.
        /// </summary>
        public static void Draw(DrawingContext ctx, Point center, double width, double rotation, double flip, double opacity,
            bool faceUp, Resource? resource = null, ActionCard? dev = null, bool isDev = false)
        {
            double w = width, h = width * Aspect;
            flip = Math.Max(0.02, Math.Abs(flip));
            var transform = Matrix.CreateScale(flip, 1) * Matrix.CreateRotation(rotation) * Matrix.CreateTranslation(center.X, center.Y);

            using (ctx.PushOpacity(Math.Clamp(opacity, 0, 1)))
            using (ctx.PushTransform(transform))
            {
                var rect = new Rect(-w / 2, -h / 2, w, h);
                double radius = w * 0.12;

                // Soft drop shadow lifts the card off whatever is under it.
                ctx.DrawRectangle(Palette.Brush(Color.FromArgb(70, 0, 0, 0)), null, rect.Translate(new Vector(w * 0.05, w * 0.08)), radius, radius);

                if (!faceUp)
                {
                    DrawBack(ctx, rect, radius, isDev);
                    return;
                }

                if (isDev) DrawDevFace(ctx, rect, radius, dev);
                else if (resource.HasValue) DrawResourceFace(ctx, rect, radius, resource.Value);
                else DrawBack(ctx, rect, radius, false);
            }
        }

        static void DrawBack(DrawingContext ctx, Rect rect, double radius, bool isDev)
        {
            Color baseColor = isDev ? DevFace : Back;
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Palette.Darken(baseColor, 1.0), 0), new GradientStop(Palette.Darken(baseColor, 0.7), 1) },
            };
            ctx.DrawRectangle(fill, new Pen(Palette.Brush(Colors.White), Math.Max(1, rect.Width * 0.05)), rect, radius, radius);
            Rect inner = rect.Deflate(rect.Width * 0.14);
            ctx.DrawRectangle(null, new Pen(Palette.Brush(BackInk), Math.Max(1, rect.Width * 0.03)), inner, radius * 0.6, radius * 0.6);

            // A small hexagon emblem in the middle.
            double r = rect.Width * 0.22;
            var hex = new StreamGeometry();
            using (var g = hex.Open())
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 3 * i - Math.PI / 2;
                    var p = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                    if (i == 0) g.BeginFigure(p, true);
                    else g.LineTo(p);
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(Palette.Brush(BackInk), null, hex);
        }

        /// <summary>A resource card: the terrain's landscape framed in cream, with the name on a ribbon.</summary>
        static void DrawResourceFace(DrawingContext ctx, Rect rect, double radius, Resource r)
        {
            Color c = Palette.Resource(r);
            ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xff, 0xf8, 0xe6)), new Pen(Palette.Brush(Palette.Darken(c, 0.5)), Math.Max(1, rect.Width * 0.045)), rect, radius, radius);
            Rect inner = rect.Deflate(Math.Max(1.5, rect.Width * 0.08));
            double ir = Math.Max(1, radius * 0.6);
            using (ctx.PushClip(new RoundedRect(inner, ir)))
            {
                TileArt.DrawInRect(ctx, r, 1, inner);
            }
            ctx.DrawRectangle(null, new Pen(Palette.Brush(Color.FromArgb(120, 0, 0, 0)), Math.Max(0.8, rect.Width * 0.02)), inner, ir, ir);
            if (rect.Width >= 30)
            {
                // A ribbon across the bottom carries the name.
                double bh = rect.Width * 0.26;
                var band = new Rect(rect.Left + rect.Width * 0.04, inner.Bottom - bh - rect.Width * 0.04, rect.Width * 0.92, bh);
                ctx.DrawRectangle(Palette.Brush(Palette.Darken(c, 0.62)), new Pen(Palette.Brush(Color.FromRgb(0xff, 0xf8, 0xe6)), Math.Max(1, rect.Width * 0.03)), band, bh / 2, bh / 2);
                var ft = new FormattedText(r.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold,
                    Math.Max(5, rect.Width * 0.17), Palette.Brush(Colors.White));
                ctx.DrawText(ft, new Point(-ft.Width / 2, band.Center.Y - ft.Height / 2));
            }
        }

        static void DrawDevFace(DrawingContext ctx, Rect rect, double radius, ActionCard? card)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0xf6, 0xec, 0xd2), 0), new GradientStop(Color.FromRgb(0xdc, 0xc8, 0x9a), 1) },
            };
            ctx.DrawRectangle(fill, new Pen(Palette.Brush(DevFace), Math.Max(1, rect.Width * 0.06)), rect, radius, radius);
            string title = card switch
            {
                ActionCard.Soldier => "Soldier",
                ActionCard.VictoryPoint => "Victory\nPoint",
                ActionCard.Engineers => "Engineers",
                ActionCard.Harvest => "Harvest",
                ActionCard.Plunder => "Plunder",
                _ => "?",
            };
            DrawDevIcon(ctx, new Point(0, -rect.Height * 0.14), rect.Width * 0.5, card);
            var t = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold,
                Math.Max(5, rect.Width * 0.14), Palette.Brush(Palette.Ink)) { TextAlignment = TextAlignment.Center, MaxTextWidth = rect.Width };
            ctx.DrawText(t, new Point(rect.Left, rect.Height * 0.14));
        }

        static void DrawDevIcon(DrawingContext ctx, Point c, double size, ActionCard? card)
        {
            double u = size / 2;
            var ink = Palette.Brush(DevFace);
            var gold = Palette.Brush(Color.FromRgb(0xd9, 0xa4, 0x2b));
            switch (card)
            {
                case ActionCard.Soldier: // a shield
                    ctx.DrawGeometry(ink, null, Poly(new Point(c.X - u * 0.8, c.Y - u * 0.8), new Point(c.X + u * 0.8, c.Y - u * 0.8),
                        new Point(c.X + u * 0.75, c.Y + u * 0.1), new Point(c.X, c.Y + u * 0.9), new Point(c.X - u * 0.75, c.Y + u * 0.1)));
                    ctx.DrawRectangle(gold, null, new Rect(c.X - u * 0.1, c.Y - u * 0.6, u * 0.2, u * 1.1));
                    ctx.DrawRectangle(gold, null, new Rect(c.X - u * 0.5, c.Y - u * 0.25, u * 1.0, u * 0.2));
                    break;
                case ActionCard.VictoryPoint: // a star
                    var pts = new Point[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = Math.PI / 5 * i - Math.PI / 2, r = i % 2 == 0 ? u : u * 0.42;
                        pts[i] = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
                    }
                    ctx.DrawGeometry(gold, new Pen(ink, Math.Max(1, u * 0.08)), Poly(pts));
                    break;
                case ActionCard.Engineers: // two roads
                    var road = new Pen(ink, u * 0.28, lineCap: PenLineCap.Round);
                    ctx.DrawLine(road, new Point(c.X - u * 0.8, c.Y + u * 0.6), new Point(c.X - u * 0.1, c.Y - u * 0.6));
                    ctx.DrawLine(road, new Point(c.X + u * 0.1, c.Y + u * 0.6), new Point(c.X + u * 0.8, c.Y - u * 0.6));
                    break;
                case ActionCard.Harvest: // a sheaf: three grains
                    for (int k = -1; k <= 1; k++)
                        ctx.DrawEllipse(k == 0 ? gold : ink, null, new Point(c.X + k * u * 0.5, c.Y + Math.Abs(k) * u * 0.2), u * 0.3, u * 0.6);
                    break;
                case ActionCard.Plunder: // a crown
                    ctx.DrawGeometry(gold, new Pen(ink, Math.Max(1, u * 0.08)), Poly(new Point(c.X - u * 0.9, c.Y + u * 0.6), new Point(c.X - u * 0.9, c.Y - u * 0.5),
                        new Point(c.X - u * 0.45, c.Y), new Point(c.X, c.Y - u * 0.8), new Point(c.X + u * 0.45, c.Y),
                        new Point(c.X + u * 0.9, c.Y - u * 0.5), new Point(c.X + u * 0.9, c.Y + u * 0.6)));
                    break;
                default:
                    var q = new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold,
                        Math.Max(6, size * 1.2), ink);
                    ctx.DrawText(q, new Point(c.X - q.Width / 2, c.Y - q.Height / 2));
                    break;
            }
        }

        static void DrawLabel(DrawingContext ctx, string text, Rect rect, Color color)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold,
                Math.Max(5, rect.Width * 0.17), Palette.Brush(color));
            ctx.DrawText(ft, new Point(-ft.Width / 2, rect.Bottom - rect.Width * 0.12 - ft.Height));
        }

        /// <summary>A small picture of each resource, centered on <paramref name="c"/>, roughly <paramref name="size"/> wide.</summary>
        public static void DrawResourceIcon(DrawingContext ctx, Point c, double size, Resource r)
        {
            double u = size / 2;
            var ink = Palette.Brush(Color.FromArgb(235, 255, 251, 238));
            var dark = Palette.Brush(Color.FromArgb(150, 0, 0, 0));
            switch (r)
            {
                case Resource.Wood:
                    // Two pines.
                    Tree(ctx, new Point(c.X - u * 0.35, c.Y + u * 0.1), u * 0.75, ink, dark);
                    Tree(ctx, new Point(c.X + u * 0.35, c.Y - u * 0.05), u * 0.9, ink, dark);
                    break;
                case Resource.Brick:
                    for (int row = 0; row < 3; row++)
                    {
                        double y = c.Y - u * 0.55 + row * u * 0.42;
                        double off = row % 2 == 0 ? 0 : u * 0.38;
                        for (int k = -1; k <= 1; k++)
                        {
                            double x = c.X + k * u * 0.76 + off - u * 0.19;
                            if (x - u * 0.34 < c.X - u * 1.0 || x + u * 0.34 > c.X + u * 1.0) continue;
                            ctx.DrawRectangle(ink, null, new Rect(x - u * 0.34, y, u * 0.68, u * 0.34), u * 0.04, u * 0.04);
                        }
                    }
                    break;
                case Resource.Sheep:
                    ctx.DrawEllipse(ink, null, new Point(c.X, c.Y), u * 0.7, u * 0.48);
                    ctx.DrawEllipse(ink, null, new Point(c.X - u * 0.4, c.Y - u * 0.2), u * 0.35, u * 0.33);
                    ctx.DrawEllipse(ink, null, new Point(c.X + u * 0.35, c.Y - u * 0.25), u * 0.35, u * 0.33);
                    ctx.DrawEllipse(dark, null, new Point(c.X + u * 0.78, c.Y - u * 0.05), u * 0.22, u * 0.18);
                    ctx.DrawLine(new Pen(dark, u * 0.1), new Point(c.X - u * 0.35, c.Y + u * 0.4), new Point(c.X - u * 0.35, c.Y + u * 0.75));
                    ctx.DrawLine(new Pen(dark, u * 0.1), new Point(c.X + u * 0.35, c.Y + u * 0.4), new Point(c.X + u * 0.35, c.Y + u * 0.75));
                    break;
                case Resource.Wheat:
                    var stalk = new Pen(ink, Math.Max(1, u * 0.08));
                    for (int k = -1; k <= 1; k++)
                    {
                        var top = new Point(c.X + k * u * 0.38, c.Y - u * 0.75);
                        var bottom = new Point(c.X + k * u * 0.12, c.Y + u * 0.8);
                        ctx.DrawLine(stalk, top, bottom);
                        for (int g = 0; g < 4; g++)
                        {
                            var p = new Point(top.X + (bottom.X - top.X) * g * 0.12, top.Y + u * 0.15 + g * u * 0.2);
                            ctx.DrawEllipse(ink, null, new Point(p.X - u * 0.1, p.Y), u * 0.09, u * 0.15);
                            ctx.DrawEllipse(ink, null, new Point(p.X + u * 0.1, p.Y), u * 0.09, u * 0.15);
                        }
                    }
                    break;
                case Resource.Stone:
                    var mountain = Poly(new Point(c.X - u, c.Y + u * 0.7), new Point(c.X - u * 0.25, c.Y - u * 0.6),
                        new Point(c.X + u * 0.15, c.Y - u * 0.05), new Point(c.X + u * 0.45, c.Y - u * 0.4), new Point(c.X + u, c.Y + u * 0.7));
                    ctx.DrawGeometry(ink, null, mountain);
                    ctx.DrawGeometry(dark, null, Poly(new Point(c.X - u * 0.25, c.Y - u * 0.6), new Point(c.X - u * 0.05, c.Y - u * 0.25),
                        new Point(c.X - u * 0.25, c.Y - u * 0.3), new Point(c.X - u * 0.45, c.Y - u * 0.2)));
                    break;
            }
        }

        static void Tree(DrawingContext ctx, Point bottom, double h, IBrush ink, IBrush dark)
        {
            ctx.DrawRectangle(dark, null, new Rect(bottom.X - h * 0.07, bottom.Y - h * 0.05, h * 0.14, h * 0.3));
            ctx.DrawGeometry(ink, null, Poly(new Point(bottom.X - h * 0.45, bottom.Y), new Point(bottom.X, bottom.Y - h * 0.85), new Point(bottom.X + h * 0.45, bottom.Y)));
        }

        static Geometry Poly(params Point[] pts)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
                g.EndFigure(true);
            }
            return geo;
        }

        public static Color Lighten(Color c, double t) =>
            Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    }
}
