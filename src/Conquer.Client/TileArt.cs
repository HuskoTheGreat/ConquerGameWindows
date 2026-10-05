using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// Cartoon landscapes for the board tiles: a forest for Wood, a clay pit for Brick, a pasture with sheep, a
    /// wheat field, snowy mountains for Stone and dunes for the wasteland. Each picture is built once, in the
    /// unit hex (pointy top, corner radius 1), as a handful of merged geometries, and is drawn at any size with a
    /// transform and a hex clip, so a board of any radius costs a few draw calls per tile.
    /// </summary>
    public static class TileArt
    {
        public const int Variants = 3;
        static readonly double Sqrt3 = Math.Sqrt(3.0);

        /// <summary>The unit hex, pointy top, corner radius 1, centered on the origin.</summary>
        public static readonly Geometry UnitHex = HexGeometry(1.0);

        sealed class Layer
        {
            public Geometry Geometry;
            public IBrush Fill;
            public IPen Pen;
        }

        static readonly Dictionary<int, Layer[]> Cache = new Dictionary<int, Layer[]>();

        /// <summary>A stable picture variant for a tile, so neighbouring forests don't look stamped.</summary>
        public static int VariantOf(Hex hex) => (((hex.Q * 7 + hex.R * 13) % Variants) + Variants) % Variants;

        /// <summary>Draws the terrain filling a hex centered on <paramref name="center"/> with corner radius <paramref name="radius"/>.</summary>
        public static void DrawHex(DrawingContext ctx, Resource terrain, int variant, Point center, double radius)
        {
            Layer[] layers = Get(terrain, variant);
            using (ctx.PushTransform(Matrix.CreateScale(radius, radius) * Matrix.CreateTranslation(center.X, center.Y)))
            using (ctx.PushGeometryClip(UnitHex))
            {
                foreach (Layer l in layers) ctx.DrawGeometry(l.Fill, l.Pen, l.Geometry);
            }
        }

        /// <summary>Draws the terrain as a picture filling <paramref name="rect"/> (for card faces); the caller clips.</summary>
        public static void DrawInRect(DrawingContext ctx, Resource terrain, int variant, Rect rect)
        {
            Layer[] layers = Get(terrain, variant);
            // Fit the hex's busy middle (about 1.5 x 1.7 units) to the rectangle.
            double scale = Math.Max(rect.Width / 1.45, rect.Height / 1.7);
            using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(rect.Center.X, rect.Center.Y)))
            {
                foreach (Layer l in layers) ctx.DrawGeometry(l.Fill, l.Pen, l.Geometry);
            }
        }

        static Layer[] Get(Resource terrain, int variant)
        {
            int key = (int)terrain * 16 + variant;
            if (Cache.TryGetValue(key, out Layer[] layers)) return layers;
            var art = new Art(new Random(1234 + key * 977));
            switch (terrain)
            {
                case Resource.Wood: Forest(art, variant); break;
                case Resource.Brick: ClayPit(art, variant); break;
                case Resource.Sheep: Pasture(art, variant); break;
                case Resource.Wheat: Field(art, variant); break;
                case Resource.Stone: Mountains(art, variant); break;
                default: Desert(art, variant); break;
            }
            layers = art.Finish();
            Cache[key] = layers;
            return layers;
        }

        // ---- Terrains ------------------------------------------------------------------------------

        static void Forest(Art a, int variant)
        {
            a.Base(C(0x7c, 0xc4, 0x52), C(0x3f, 0x8a, 0x33));
            // Mossy patches on the forest floor.
            a.Paint(C(0x5f, 0xa8, 0x40));
            for (int i = 0; i < 6; i++)
            {
                Point p = a.Anywhere(0.85);
                a.Ellipse(p.X, p.Y, 0.12 + a.R.NextDouble() * 0.08, 0.05 + a.R.NextDouble() * 0.03);
            }

            List<Point> spots = a.Scatter(10 + variant, 0.82, 0.25, 0.36);
            spots.Sort((p, q) => p.Y.CompareTo(q.Y));
            // Trees in three depth bands, each band drawn trunk, crown, shading, so nearer trees overlap farther ones.
            for (int band = 0; band < 3; band++)
            {
                double lo = -1 + band * 0.7, hi = lo + 0.7;
                var band0 = spots.FindAll(p => p.Y >= lo && (p.Y < hi || band == 2));
                if (band0.Count == 0) continue;
                var kinds = new List<(Point P, bool Pine, double H)>();
                foreach (Point p in band0) kinds.Add((p, a.R.NextDouble() < 0.68, 0.34 + a.R.NextDouble() * 0.1));

                a.Paint(C(0x8a, 0x55, 0x2c), C(0x3b, 0x22, 0x10), 0.03);
                foreach (var t in kinds) a.Rect(t.P.X - 0.03, t.P.Y - 0.06, 0.06, 0.1, 0.01);

                a.Paint(C(0x2f, 0x93, 0x48), C(0x16, 0x4a, 0x24), 0.035);
                foreach (var t in kinds) if (t.Pine) Pine(a, t.P, t.H, shade: false);
                a.Paint(C(0x5c, 0xbf, 0x45), C(0x22, 0x5e, 0x1e), 0.035);
                foreach (var t in kinds) if (!t.Pine) a.Ellipse(t.P.X, t.P.Y - t.H * 0.42, t.H * 0.33, t.H * 0.36);

                a.Paint(C(0x22, 0x77, 0x39));
                foreach (var t in kinds) if (t.Pine) Pine(a, t.P, t.H, shade: true);
                a.Paint(C(0x92, 0xdb, 0x6c));
                foreach (var t in kinds) if (!t.Pine) a.Ellipse(t.P.X - t.H * 0.1, t.P.Y - t.H * 0.55, t.H * 0.12, t.H * 0.1);
            }
        }

        /// <summary>A three-tier pine standing on <paramref name="foot"/>; <paramref name="shade"/> draws only its shadowed right half.</summary>
        static void Pine(Art a, Point foot, double h, bool shade)
        {
            for (int k = 0; k < 3; k++)
            {
                double bottom = foot.Y - 0.04 - k * h * 0.24;
                double top = bottom - h * 0.46;
                double half = h * (0.36 - k * 0.07);
                if (shade) a.Poly(new Point(foot.X, top), new Point(foot.X + half, bottom), new Point(foot.X + half * 0.1, bottom));
                else a.Poly(new Point(foot.X, top), new Point(foot.X + half, bottom), new Point(foot.X - half, bottom));
            }
        }

        static void ClayPit(Art a, int variant)
        {
            a.Base(C(0xec, 0x9a, 0x5c), C(0xc4, 0x63, 0x33));
            // Strata in the clay.
            a.Paint(C(0xd9, 0x7d, 0x46));
            for (int i = 0; i < 4; i++)
            {
                double y = -0.8 + i * 0.45 + a.R.NextDouble() * 0.1;
                a.Wave(y, 0.05, 0.06, a.R.NextDouble() * 6);
            }

            // The pit: a dark hollow with a pale rim.
            double px = variant == 1 ? 0.3 : -0.32, py = variant == 2 ? -0.45 : 0.5;
            a.Paint(C(0xf3, 0xb4, 0x7f), C(0x7a, 0x37, 0x18), 0.03);
            a.Ellipse(px, py, 0.34, 0.17);
            a.Paint(C(0x9a, 0x45, 0x20));
            a.Ellipse(px, py + 0.02, 0.26, 0.11);
            a.Paint(C(0x7a, 0x33, 0x15));
            a.Ellipse(px + 0.03, py + 0.04, 0.17, 0.06);

            // Stacks of bricks.
            var stacks = new List<Point>();
            Point[] candidates = { new Point(0.36, -0.42), new Point(-0.4, -0.38), new Point(0.42, 0.5), new Point(-0.45, 0.42), new Point(0.0, -0.66), new Point(0.02, 0.72) };
            foreach (Point c in candidates)
            {
                if (Math.Abs(c.X - px) < 0.45 && Math.Abs(c.Y - py) < 0.3) continue;
                stacks.Add(c);
                if (stacks.Count == 3) break;
            }
            const double bw = 0.15, bh = 0.075;
            a.Paint(C(0xc2, 0x47, 0x2b), C(0x55, 0x1a, 0x0c), 0.025);
            foreach (Point s in stacks)
            {
                for (int row = 0; row < 3; row++)
                {
                    int n = 3 - row;
                    double y = s.Y - row * bh;
                    for (int i = 0; i < n; i++)
                        a.Rect(s.X - n * bw / 2 + i * bw + 0.005, y - bh, bw - 0.01, bh - 0.008, 0.012);
                }
            }
            a.Paint(C(0xe8, 0x7a, 0x5a));
            foreach (Point s in stacks)
            {
                for (int row = 0; row < 3; row++)
                {
                    int n = 3 - row;
                    double y = s.Y - row * bh;
                    for (int i = 0; i < n; i++)
                        a.Rect(s.X - n * bw / 2 + i * bw + 0.02, y - bh + 0.01, bw - 0.05, 0.016, 0.006);
                }
            }

            // Pebbles of clay.
            a.Paint(C(0xb0, 0x58, 0x2e), C(0x6e, 0x2c, 0x12), 0.02);
            for (int i = 0; i < 5; i++)
            {
                Point p = a.Anywhere(0.8, 0.4);
                a.Ellipse(p.X, p.Y, 0.04, 0.03);
            }
        }

        static void Pasture(Art a, int variant)
        {
            a.Base(C(0xb5, 0xe6, 0x6e), C(0x7c, 0xc4, 0x44));
            a.Paint(C(0xc8, 0xef, 0x8a));
            for (int i = 0; i < 4; i++)
            {
                Point p = a.Anywhere(0.8);
                a.Ellipse(p.X, p.Y, 0.22, 0.09);
            }

            // Grass tufts.
            a.Stroke(C(0x4c, 0x99, 0x2c), 0.025);
            for (int i = 0; i < 16; i++)
            {
                Point p = a.Anywhere(0.9, 0.3);
                a.Line(new Point(p.X - 0.05, p.Y - 0.05), new Point(p.X - 0.02, p.Y), new Point(p.X, p.Y - 0.07), new Point(p.X + 0.02, p.Y), new Point(p.X + 0.05, p.Y - 0.05));
            }
            // Little flowers.
            a.Paint(C(0xff, 0xff, 0xff));
            var flowers = new List<Point>();
            for (int i = 0; i < 7; i++) flowers.Add(a.Anywhere(0.85, 0.36));
            foreach (Point p in flowers) a.Ellipse(p.X, p.Y, 0.025, 0.025);
            a.Paint(C(0xff, 0xd2, 0x3a));
            foreach (Point p in flowers) a.Ellipse(p.X, p.Y, 0.011, 0.011);

            // Fluffy sheep.
            var sheep = a.Scatter(3 + (variant == 2 ? 1 : 0), 0.72, 0.45, 0.5);
            var flips = new List<bool>();
            foreach (Point _ in sheep) flips.Add(a.R.NextDouble() < 0.5);
            a.Stroke(C(0x33, 0x33, 0x38), 0.035);
            foreach (Point s in sheep)
            {
                a.Line(new Point(s.X - 0.06, s.Y + 0.04), new Point(s.X - 0.065, s.Y + 0.12));
                a.Line(new Point(s.X + 0.06, s.Y + 0.04), new Point(s.X + 0.065, s.Y + 0.12));
            }
            a.Paint(C(0xfd, 0xfd, 0xf8), C(0x4a, 0x4a, 0x52), 0.03);
            foreach (Point s in sheep)
            {
                a.Ellipse(s.X, s.Y, 0.12, 0.075);
                a.Ellipse(s.X - 0.07, s.Y - 0.04, 0.06, 0.055);
                a.Ellipse(s.X + 0.0, s.Y - 0.06, 0.065, 0.055);
                a.Ellipse(s.X + 0.07, s.Y - 0.035, 0.06, 0.055);
                a.Ellipse(s.X - 0.08, s.Y + 0.03, 0.05, 0.045);
                a.Ellipse(s.X + 0.08, s.Y + 0.03, 0.05, 0.045);
            }
            a.Paint(C(0x3a, 0x36, 0x3c), C(0x1f, 0x1c, 0x20), 0.02);
            for (int i = 0; i < sheep.Count; i++)
            {
                double dir = flips[i] ? -1 : 1;
                Point s = sheep[i];
                a.Ellipse(s.X + dir * 0.13, s.Y - 0.035, 0.045, 0.055);
                a.Ellipse(s.X + dir * 0.1, s.Y - 0.075, 0.025, 0.014);
            }
            a.Paint(C(0xff, 0xff, 0xff));
            for (int i = 0; i < sheep.Count; i++)
            {
                double dir = flips[i] ? -1 : 1;
                a.Ellipse(sheep[i].X + dir * 0.145, sheep[i].Y - 0.045, 0.011, 0.011);
            }
        }

        static void Field(Art a, int variant)
        {
            a.Base(C(0xfa, 0xda, 0x6a), C(0xe6, 0xa9, 0x2e));
            // Ploughed rows running across the field, alternating light and dark.
            double angle = (variant - 1) * 0.35 - 0.3;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            Point Rot(double x, double y) => new Point(x * cos - y * sin, x * sin + y * cos);
            a.Paint(C(0xee, 0xb9, 0x3c));
            for (int i = -6; i <= 6; i += 2)
            {
                double y0 = i * 0.16, y1 = y0 + 0.16;
                a.Poly(Rot(-1.6, y0), Rot(1.6, y0), Rot(1.6, y1), Rot(-1.6, y1));
            }

            // Stalks along each row: a stem and an ear of grains.
            var ears = new List<(Point Top, double Lean)>();
            for (int i = -6; i <= 6; i++)
            {
                double y = i * 0.16 + 0.12;
                for (double x = -1.2 + (i & 1) * 0.09; x < 1.2; x += 0.18)
                {
                    Point foot = Rot(x + (a.R.NextDouble() - 0.5) * 0.05, y);
                    if (!InHex(foot, 0.92) || foot.X * foot.X + foot.Y * foot.Y < 0.12) continue;
                    ears.Add((foot, (a.R.NextDouble() - 0.5) * 0.06));
                }
            }
            a.Stroke(C(0xb0, 0x76, 0x18), 0.02);
            foreach (var e in ears) a.Line(e.Top, new Point(e.Top.X + e.Lean, e.Top.Y - 0.16));
            a.Paint(C(0xff, 0xd8, 0x4d), C(0x9c, 0x63, 0x10), 0.018);
            foreach (var e in ears)
            {
                double x = e.Top.X + e.Lean, y = e.Top.Y - 0.16;
                for (int g = 0; g < 3; g++)
                {
                    a.Ellipse(x - 0.022, y + 0.02 + g * 0.035, 0.018, 0.026);
                    a.Ellipse(x + 0.022, y + 0.02 + g * 0.035, 0.018, 0.026);
                }
                a.Ellipse(x, y - 0.005, 0.016, 0.026);
            }
        }

        static void Mountains(Art a, int variant)
        {
            a.Base(C(0xc6, 0xcd, 0xd6), C(0x93, 0x9c, 0xab));
            var ink = C(0x45, 0x4b, 0x5a);

            // Two or three peaks across the top, the back one tallest.
            var peaks = new List<(double X, double Base, double Top, double Half)>
            {
                (0.2 - variant * 0.12, 0.25, -0.86, 0.62),
                (-0.42 + variant * 0.05, 0.3, -0.5, 0.48),
                (0.55, 0.38, -0.32, 0.4),
            };
            foreach (var m in peaks)
            {
                a.Paint(C(0x9a, 0xa3, 0xb3), ink, 0.035);
                a.Poly(new Point(m.X - m.Half, m.Base), new Point(m.X - m.Half * 0.12, m.Top + 0.03), new Point(m.X, m.Top),
                    new Point(m.X + m.Half * 0.12, m.Top + 0.04), new Point(m.X + m.Half, m.Base));
                a.Paint(C(0x76, 0x7f, 0x91));
                a.Poly(new Point(m.X, m.Top), new Point(m.X + m.Half * 0.12, m.Top + 0.04), new Point(m.X + m.Half, m.Base), new Point(m.X + m.Half * 0.15, m.Base));
                // Snow cap with a zigzag hem.
                double h = (m.Base - m.Top) * 0.32, w = m.Half * 0.32;
                a.Paint(C(0xfb, 0xfd, 0xff), ink, 0.03);
                a.Poly(new Point(m.X, m.Top), new Point(m.X + m.Half * 0.12, m.Top + 0.04), new Point(m.X + w, m.Top + h),
                    new Point(m.X + w * 0.4, m.Top + h * 0.75), new Point(m.X, m.Top + h * 1.05), new Point(m.X - w * 0.45, m.Top + h * 0.75),
                    new Point(m.X - w, m.Top + h), new Point(m.X - m.Half * 0.12, m.Top + 0.03));
            }

            // Boulders scattered in front.
            var rocks = a.Scatter(5, 0.8, 0.42, 0.3);
            rocks.RemoveAll(p => p.Y < 0.05);
            if (rocks.Count < 3)
            {
                rocks.Add(new Point(-0.45, 0.55));
                rocks.Add(new Point(0.4, 0.6));
                rocks.Add(new Point(0.0, 0.8));
            }
            var sizes = new List<double>();
            foreach (Point _ in rocks) sizes.Add(0.09 + a.R.NextDouble() * 0.06);
            a.Paint(C(0xa9, 0xb1, 0xbe), ink, 0.03);
            for (int i = 0; i < rocks.Count; i++) a.Blob(rocks[i], sizes[i], sizes[i] * 0.72, i);
            a.Paint(C(0xdb, 0xe0, 0xe8));
            for (int i = 0; i < rocks.Count; i++) a.Ellipse(rocks[i].X - sizes[i] * 0.35, rocks[i].Y - sizes[i] * 0.3, sizes[i] * 0.28, sizes[i] * 0.16);
            a.Paint(C(0x80, 0x88, 0x97));
            for (int i = 0; i < rocks.Count; i++) a.Ellipse(rocks[i].X + sizes[i] * 0.3, rocks[i].Y + sizes[i] * 0.35, sizes[i] * 0.4, sizes[i] * 0.16);
        }

        static void Desert(Art a, int variant)
        {
            a.Base(C(0xfb, 0xe7, 0xaa), C(0xe9, 0xc6, 0x76));
            // Rolling dunes, each lower band a shade deeper.
            Color[] dunes = { C(0xf2, 0xd5, 0x8e), C(0xea, 0xc4, 0x75), C(0xdf, 0xb2, 0x62) };
            for (int i = 0; i < 3; i++)
            {
                double y = -0.45 + i * 0.5;
                a.Paint(dunes[i], C(0xc9, 0x96, 0x45), 0.025);
                a.Dune(y, 0.12, 2.2 + i * 0.6, variant * 1.3 + i * 2.1);
            }

            // A saguaro cactus.
            double cx = variant == 0 ? -0.35 : variant == 1 ? 0.38 : -0.1, cy = variant == 2 ? 0.62 : 0.38;
            a.Paint(C(0x6c, 0xbf, 0x55), C(0x25, 0x5e, 0x22), 0.035);
            a.Rect(cx - 0.06, cy - 0.5, 0.12, 0.52, 0.06);
            a.Rect(cx - 0.21, cy - 0.32, 0.08, 0.2, 0.04);
            a.Rect(cx - 0.21, cy - 0.17, 0.18, 0.07, 0.035);
            a.Rect(cx + 0.13, cy - 0.4, 0.08, 0.22, 0.04);
            a.Rect(cx + 0.03, cy - 0.24, 0.18, 0.07, 0.035);
            a.Stroke(C(0x4c, 0x9a, 0x3c), 0.015);
            a.Line(new Point(cx - 0.015, cy - 0.44), new Point(cx - 0.015, cy - 0.02));
            a.Line(new Point(cx + 0.025, cy - 0.44), new Point(cx + 0.025, cy - 0.02));

            // Sun-baked rocks and a bleached branch.
            a.Paint(C(0xc8, 0x9f, 0x6a), C(0x6e, 0x4c, 0x26), 0.03);
            a.Blob(new Point(cx > 0 ? -0.42 : 0.42, 0.62), 0.12, 0.08, 1);
            a.Blob(new Point(cx > 0 ? -0.25 : 0.25, 0.7), 0.07, 0.05, 2);
            a.Blob(new Point(variant == 1 ? -0.3 : 0.35, -0.45), 0.08, 0.055, 3);
            a.Stroke(C(0xff, 0xf2, 0xc9), 0.03);
            a.Line(new Point(cx > 0 ? 0.0 : 0.1, -0.7), new Point(cx > 0 ? 0.16 : 0.26, -0.62), new Point(cx > 0 ? 0.22 : 0.32, -0.68));
        }

        // ---- Geometry helpers ----------------------------------------------------------------------

        static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

        static bool InHex(Point p, double scale) =>
            Math.Abs(p.X) <= Sqrt3 / 2 * scale && Math.Abs(p.X) * 0.5 + Math.Abs(p.Y) * Sqrt3 / 2 <= Sqrt3 / 2 * scale;

        public static Geometry HexGeometry(double radius)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 180 * (60 * i - 90);
                    var p = new Point(radius * Math.Cos(a), radius * Math.Sin(a));
                    if (i == 0) g.BeginFigure(p, true);
                    else g.LineTo(p);
                }
                g.EndFigure(true);
            }
            return geo;
        }

        /// <summary>
        /// Collects shapes into layers. Shapes painted with an outline are drawn twice (outline, then fill on top),
        /// so overlapping shapes in one layer share a single silhouette outline, the way a cartoon is inked.
        /// </summary>
        sealed class Art
        {
            public readonly Random R;
            readonly List<Layer> _layers = new List<Layer>();
            StreamGeometry _geo;
            StreamGeometryContext _ctx;
            IBrush _fill;
            IPen _pen;
            bool _strokeOnly;

            public Art(Random r) => R = r;

            public void Base(Color top, Color bottom)
            {
                Close();
                var geo = new RectangleGeometry(new Rect(-1.2, -1.2, 2.4, 2.4));
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.3, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
                };
                _layers.Add(new Layer { Geometry = geo, Fill = brush });
            }

            public void Paint(Color fill, Color? outline = null, double width = 0)
            {
                Close();
                _fill = new ImmutableSolidColorBrush(fill);
                _pen = outline.HasValue ? new ImmutablePen(new ImmutableSolidColorBrush(outline.Value), width * 2, null, PenLineCap.Round, PenLineJoin.Round) : null;
                _strokeOnly = false;
            }

            public void Stroke(Color color, double width)
            {
                Close();
                _fill = null;
                _pen = new ImmutablePen(new ImmutableSolidColorBrush(color), width, null, PenLineCap.Round, PenLineJoin.Round);
                _strokeOnly = true;
            }

            StreamGeometryContext G()
            {
                if (_ctx == null)
                {
                    _geo = new StreamGeometry();
                    _ctx = _geo.Open();
                    _ctx.SetFillRule(FillRule.NonZero);
                }
                return _ctx;
            }

            void Close()
            {
                if (_ctx == null) return;
                _ctx.Dispose();
                _ctx = null;
                if (_strokeOnly) _layers.Add(new Layer { Geometry = _geo, Pen = _pen });
                else
                {
                    if (_pen != null) _layers.Add(new Layer { Geometry = _geo, Fill = _fill, Pen = _pen });
                    _layers.Add(new Layer { Geometry = _geo, Fill = _fill });
                }
                _geo = null;
            }

            public Layer[] Finish()
            {
                Close();
                return _layers.ToArray();
            }

            public void Ellipse(double cx, double cy, double rx, double ry)
            {
                StreamGeometryContext g = G();
                g.BeginFigure(new Point(cx - rx, cy), true);
                g.ArcTo(new Point(cx + rx, cy), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
                g.ArcTo(new Point(cx - rx, cy), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
                g.EndFigure(true);
            }

            /// <summary>A closed polygon, always wound clockwise so overlapping shapes merge instead of cutting holes.</summary>
            public void Poly(params Point[] pts)
            {
                double area = 0;
                for (int i = 0; i < pts.Length; i++)
                {
                    Point p = pts[i], q = pts[(i + 1) % pts.Length];
                    area += p.X * q.Y - q.X * p.Y;
                }
                if (area < 0) Array.Reverse(pts);
                StreamGeometryContext g = G();
                g.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
                g.EndFigure(true);
            }

            public void Line(params Point[] pts)
            {
                StreamGeometryContext g = G();
                g.BeginFigure(pts[0], false);
                for (int i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
                g.EndFigure(false);
            }

            public void Rect(double x, double y, double w, double h, double r)
            {
                r = Math.Min(r, Math.Min(w, h) / 2);
                StreamGeometryContext g = G();
                var size = new Size(r, r);
                g.BeginFigure(new Point(x + r, y), true);
                g.LineTo(new Point(x + w - r, y));
                g.ArcTo(new Point(x + w, y + r), size, 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(x + w, y + h - r));
                g.ArcTo(new Point(x + w - r, y + h), size, 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(x + r, y + h));
                g.ArcTo(new Point(x, y + h - r), size, 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(x, y + r));
                g.ArcTo(new Point(x + r, y), size, 0, false, SweepDirection.Clockwise);
                g.EndFigure(true);
            }

            /// <summary>A lumpy rock: an irregular rounded polygon.</summary>
            public void Blob(Point c, double rx, double ry, int seed)
            {
                var rnd = new Random(seed * 31 + 7);
                const int n = 8;
                var pts = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    double ang = Math.PI * 2 * i / n;
                    double k = 0.85 + rnd.NextDouble() * 0.25;
                    pts[i] = new Point(c.X + Math.Cos(ang) * rx * k, c.Y + Math.Sin(ang) * ry * k * (Math.Sin(ang) > 0 ? 0.8 : 1.1));
                }
                // Smooth it with quadratic curves through the edge midpoints.
                StreamGeometryContext g = G();
                Point Mid(Point p, Point q) => new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2);
                g.BeginFigure(Mid(pts[n - 1], pts[0]), true);
                for (int i = 0; i < n; i++) g.QuadraticBezierTo(pts[i], Mid(pts[i], pts[(i + 1) % n]));
                g.EndFigure(true);
            }

            /// <summary>A thin wavy band across the whole tile, for strata.</summary>
            public void Wave(double y, double thickness, double amplitude, double phase)
            {
                StreamGeometryContext g = G();
                const int steps = 16;
                g.BeginFigure(new Point(-1.2, y + Math.Sin(phase) * amplitude), true);
                for (int i = 1; i <= steps; i++)
                {
                    double x = -1.2 + 2.4 * i / steps;
                    g.LineTo(new Point(x, y + Math.Sin(phase + x * 3) * amplitude));
                }
                for (int i = steps; i >= 0; i--)
                {
                    double x = -1.2 + 2.4 * i / steps;
                    g.LineTo(new Point(x, y + thickness + Math.Sin(phase + x * 3 + 0.6) * amplitude));
                }
                g.EndFigure(true);
            }

            /// <summary>A dune: a smooth crest across the tile, filled down past the bottom.</summary>
            public void Dune(double y, double amplitude, double frequency, double phase)
            {
                StreamGeometryContext g = G();
                const int steps = 24;
                g.BeginFigure(new Point(-1.2, y + Math.Sin(phase - 1.2 * frequency) * amplitude), true);
                for (int i = 1; i <= steps; i++)
                {
                    double x = -1.2 + 2.4 * i / steps;
                    g.LineTo(new Point(x, y + Math.Sin(phase + x * frequency) * amplitude));
                }
                g.LineTo(new Point(1.2, 1.3));
                g.LineTo(new Point(-1.2, 1.3));
                g.EndFigure(true);
            }

            /// <summary>A random point in the hex (scaled by <paramref name="reach"/>), outside a clear middle of radius <paramref name="clear"/>.</summary>
            public Point Anywhere(double reach, double clear = 0)
            {
                for (int tries = 0; tries < 200; tries++)
                {
                    var p = new Point((R.NextDouble() * 2 - 1) * 0.87 * reach, (R.NextDouble() * 2 - 1) * reach);
                    if (InHex(p, reach) && p.X * p.X + p.Y * p.Y >= clear * clear) return p;
                }
                return new Point(0, 0.7);
            }

            /// <summary>Up to <paramref name="count"/> well-spaced points in the hex, outside the middle where the number token sits.</summary>
            public List<Point> Scatter(int count, double reach, double minGap, double clear)
            {
                var pts = new List<Point>();
                for (int tries = 0; tries < 600 && pts.Count < count; tries++)
                {
                    Point p = Anywhere(reach, clear);
                    bool ok = true;
                    foreach (Point q in pts)
                    {
                        double dx = p.X - q.X, dy = p.Y - q.Y;
                        if (dx * dx + dy * dy < minGap * minGap) { ok = false; break; }
                    }
                    if (ok) pts.Add(p);
                }
                return pts;
            }
        }
    }
}
