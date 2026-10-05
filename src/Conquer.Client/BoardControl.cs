using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Conquer.Client.Animation;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// Draws the board in 2D from <see cref="Game"/> state and turns clicks into <see cref="Spot"/> picks.
    /// All geometry comes from <see cref="HexLayout"/>, so the picture and the rules can't disagree.
    /// </summary>
    public sealed class BoardControl : Control
    {
        static readonly double Sqrt3 = Math.Sqrt(3.0);
        static readonly Typeface Font = new Typeface(Ui.Font, FontStyle.Normal, FontWeight.Bold);
        static readonly Typeface FontBold = new Typeface(Ui.Font, FontStyle.Normal, FontWeight.Black);

        LocalGameController _controller;
        int _hover = -1;
        Hex? _hoverTile;

        /// <summary>Cosmetic animation state (piece pops, raider slide, roll glow). Null draws everything at rest.</summary>
        public BoardEffects Effects { get; set; }

        public LocalGameController Controller
        {
            get => _controller;
            set
            {
                if (_controller != null) _controller.Changed -= OnChanged;
                _controller = value;
                if (_controller != null) _controller.Changed += OnChanged;
                InvalidateVisual();
            }
        }

        void OnChanged()
        {
            _hover = -1;
            InvalidateVisual();
        }

        // ---- Coordinate mapping --------------------------------------------------------------------

        /// <summary>Pixels per layout unit, and the pixel position of the layout origin.</summary>
        (double Scale, double Ox, double Oy) Fit()
        {
            int r = _controller.Game.Board.Radius;
            double halfW = Sqrt3 * r + 2.1;
            double halfH = 1.5 * r + 1.8;
            double h = Math.Max(10, Bounds.Height - TopInset);
            double s = Math.Min(Bounds.Width / (2 * halfW), h / (2 * halfH));
            return (s, Bounds.Width / 2, TopInset + h / 2);
        }

        /// <summary>Space kept clear at the top for the prompt; the sea still fills it.</summary>
        public double TopInset { get; set; }

        Point ToPixel((float X, float Y) p, (double Scale, double Ox, double Oy) f) =>
            new Point(f.Ox + p.X * f.Scale, f.Oy + p.Y * f.Scale);

        /// <summary>Center of a tile in this control's coordinates, or null before the first layout.</summary>
        public Point? TileCenter(Hex hex)
        {
            if (_controller?.Game == null || Bounds.Width < 10 || Bounds.Height < 10) return null;
            return ToPixel(HexLayout.ToPlane(hex), Fit());
        }

        // ---- Rendering -----------------------------------------------------------------------------

        public override void Render(DrawingContext ctx)
        {
            var bounds = new Rect(Bounds.Size);
            var sea = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Palette.Sea, 0), new GradientStop(Palette.SeaDeep, 1) },
            };
            ctx.DrawRectangle(sea, null, bounds); // also makes the control hit-testable

            if (_controller?.Game == null || Bounds.Width < 10 || Bounds.Height < 10) return;

            Game game = _controller.Game;
            var f = Fit();

            DrawWaves(ctx, game, f);
            DrawShore(ctx, game, f);
            foreach (Port port in game.Board.Ports) DrawPort(ctx, game, port, f);
            foreach (Tile tile in game.Board.Tiles) DrawTile(ctx, tile, f);
            DrawTileHover(ctx, game, f);
            foreach (Tile tile in game.Board.Tiles) DrawGlow(ctx, game, tile, f);
            foreach (Tile tile in game.Board.Tiles) DrawToken(ctx, tile, f);

            foreach (var road in game.RoadOwners) DrawRoad(ctx, road.Key, road.Value, f);
            foreach (var b in game.Buildings) DrawBuilding(ctx, b.Key, b.Value, f);

            DrawRaider(ctx, game.RaiderHex, f);
            DrawSpots(ctx, f);
        }

        void DrawTile(DrawingContext ctx, Tile tile, (double Scale, double Ox, double Oy) f)
        {
            Point c0 = ToPixel(HexLayout.ToPlane(tile.Hex), f);
            double r = f.Scale * 0.965; // shrink a touch so neighbours show a seam
            TileArt.DrawHex(ctx, tile.Resource, TileArt.VariantOf(tile.Hex), c0, r);

            // A thick, soft cartoon outline, and a light rim along the top edges for a bit of bevel.
            Color fill = Palette.Resource(tile.Resource);
            using (ctx.PushTransform(Matrix.CreateScale(r, r) * Matrix.CreateTranslation(c0.X, c0.Y)))
            {
                ctx.DrawGeometry(null, new Pen(Palette.Brush(Palette.Darken(fill, 0.45)), Math.Max(1.6, f.Scale * 0.055) / r, lineJoin: PenLineJoin.Round), TileArt.UnitHex);
                ctx.DrawGeometry(null, new Pen(Palette.Brush(Color.FromArgb(70, 255, 255, 255)), Math.Max(1, f.Scale * 0.03) / r, lineJoin: PenLineJoin.Round), Inner);
            }
        }

        static readonly Geometry Inner = TileArt.HexGeometry(0.93);

        /// <summary>Cartoon wave marks across the open sea, kept clear of the island and cached per size.</summary>
        void DrawWaves(DrawingContext ctx, Game game, (double Scale, double Ox, double Oy) f)
        {
            if (_waves == null || _wavesFor != (Bounds.Size, game.Board))
            {
                _wavesFor = (Bounds.Size, game.Board);
                _waves = new StreamGeometry();
                double step = Math.Max(26, f.Scale * 0.95);
                var rnd = new Random(17);
                using (StreamGeometryContext g = _waves.Open())
                {
                    int row = 0;
                    for (double y = step * 0.4; y < Bounds.Height; y += step * 0.62, row++)
                    {
                        for (double x = (row % 2) * step * 0.5; x < Bounds.Width + step; x += step)
                        {
                            double px = x + (rnd.NextDouble() - 0.5) * step * 0.4, py = y + (rnd.NextDouble() - 0.5) * step * 0.25;
                            if (rnd.NextDouble() < 0.35) continue;
                            double lx = (px - f.Ox) / f.Scale, ly = (py - f.Oy) / f.Scale;
                            if (NearLand(game, lx, ly, 1.35)) continue;
                            double w = step * (0.16 + rnd.NextDouble() * 0.08);
                            // Two little humps: a classic cartoon wave.
                            g.BeginFigure(new Point(px - w, py), false);
                            g.QuadraticBezierTo(new Point(px - w / 2, py - w * 0.7), new Point(px, py));
                            g.QuadraticBezierTo(new Point(px + w / 2, py - w * 0.7), new Point(px + w, py));
                            g.EndFigure(false);
                        }
                    }
                }
            }
            ctx.DrawGeometry(null, new Pen(Palette.Brush(Color.FromArgb(120, 255, 255, 255)), Math.Max(1.6, f.Scale * 0.045), lineCap: PenLineCap.Round), _waves);
        }

        StreamGeometry _waves;
        (Size, Board) _wavesFor;

        static bool NearLand(Game game, double x, double y, double reach)
        {
            foreach (Tile t in game.Board.Tiles)
            {
                var c = HexLayout.ToPlane(t.Hex);
                double dx = c.X - x, dy = c.Y - y;
                if (dx * dx + dy * dy < reach * reach) return true;
            }
            return false;
        }

        /// <summary>A ring of surf and a sandy beach under the tiles, so the island reads as land in the sea.</summary>
        void DrawShore(DrawingContext ctx, Game game, (double Scale, double Ox, double Oy) f)
        {
            var foam = Palette.Brush(Color.FromArgb(150, 0xe6, 0xf8, 0xff));
            var sand = Palette.Brush(Color.FromRgb(0xf3, 0xdc, 0x9c));
            var sandEdge = new Pen(Palette.Brush(Color.FromRgb(0xd6, 0xb4, 0x6a)), Math.Max(1.5, f.Scale * 0.05));
            foreach (Tile t in game.Board.Tiles)
            {
                Point c = ToPixel(HexLayout.ToPlane(t.Hex), f);
                using (ctx.PushTransform(Matrix.CreateScale(f.Scale * 1.24, f.Scale * 1.24) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.DrawGeometry(foam, null, Round);
            }
            foreach (Tile t in game.Board.Tiles)
            {
                Point c = ToPixel(HexLayout.ToPlane(t.Hex), f);
                using (ctx.PushTransform(Matrix.CreateScale(f.Scale * 1.12, f.Scale * 1.12) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.DrawGeometry(null, new Pen(sandEdge.Brush, sandEdge.Thickness / (f.Scale * 1.12)), Round);
            }
            foreach (Tile t in game.Board.Tiles)
            {
                Point c = ToPixel(HexLayout.ToPlane(t.Hex), f);
                using (ctx.PushTransform(Matrix.CreateScale(f.Scale * 1.12, f.Scale * 1.12) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.DrawGeometry(sand, null, Round);
            }
        }

        /// <summary>A hex with softened corners, for the beach.</summary>
        static readonly Geometry Round = RoundHex();

        static Geometry RoundHex()
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                var pts = new Point[6];
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 180 * (60 * i - 90);
                    pts[i] = new Point(Math.Cos(a), Math.Sin(a));
                }
                Point Mid(Point p, Point q) => new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2);
                g.BeginFigure(Mid(pts[5], pts[0]), true);
                for (int i = 0; i < 6; i++) g.QuadraticBezierTo(pts[i], Mid(pts[i], pts[(i + 1) % 6]));
                g.EndFigure(true);
            }
            return geo;
        }

        /// <summary>The tile under the pointer gets a soft lift, so the board feels alive under the mouse.</summary>
        void DrawTileHover(DrawingContext ctx, Game game, (double Scale, double Ox, double Oy) f)
        {
            if (!_hoverTile.HasValue || !game.Board.IsLand(_hoverTile.Value)) return;
            Hex hex = _hoverTile.Value;
            var center = HexLayout.ToPlane(hex);
            var points = new List<Point>();
            for (int i = 0; i < 6; i++)
            {
                var c = HexLayout.ToPlane(Vertex.OfCorner(hex, i));
                points.Add(ToPixel((center.X + (c.X - center.X) * 0.93f, center.Y + (c.Y - center.Y) * 0.93f), f));
            }
            ctx.DrawGeometry(Palette.Brush(Color.FromArgb(34, 255, 255, 255)),
                new Pen(Palette.Brush(Color.FromArgb(150, 255, 248, 220)), Math.Max(1.5, f.Scale * 0.035)), Polygon(points));
        }

        /// <summary>After a roll, the tiles that pay out light up.</summary>
        void DrawGlow(DrawingContext ctx, Game game, Tile tile, (double Scale, double Ox, double Oy) f)
        {
            if (Effects == null || tile.IsWasteland || tile.Hex == game.RaiderHex) return;
            double glow = Effects.GlowFor(tile.Number);
            if (glow <= 0) return;

            var points = new List<Point>();
            for (int i = 0; i < 6; i++) points.Add(ToPixel(HexLayout.ToPlane(Vertex.OfCorner(tile.Hex, i)), f));
            ctx.DrawGeometry(Palette.Brush(Color.FromArgb((byte)(90 * glow), 255, 246, 200)),
                new Pen(Palette.Brush(Color.FromArgb((byte)(255 * glow), Palette.Highlight.R, Palette.Highlight.G, Palette.Highlight.B)), Math.Max(2, f.Scale * 0.08)),
                Polygon(points));
        }

        void DrawToken(DrawingContext ctx, Tile tile, (double Scale, double Ox, double Oy) f)
        {
            if (tile.IsWasteland) return;
            double s = f.Scale;
            Point c = ToPixel(HexLayout.ToPlane(tile.Hex), f);
            double r = s * 0.34;
            // A chunky cream disc with a dark outline and a hard little drop shadow.
            ctx.DrawEllipse(Palette.Brush(Color.FromArgb(80, 0, 0, 0)), null, new Point(c.X, c.Y + s * 0.045), r, r);
            ctx.DrawEllipse(Palette.Brush(Palette.Token), new Pen(Palette.Brush(Color.FromRgb(0x5a, 0x3e, 0x22)), Math.Max(1.5, s * 0.045)), c, r, r);
            ctx.DrawEllipse(null, new Pen(Palette.Brush(Color.FromArgb(90, 0xc8, 0xa8, 0x6a)), Math.Max(1, s * 0.02)), c, r * 0.8, r * 0.8);

            Color ink = tile.Number == 6 || tile.Number == 8 ? Palette.Hot : Palette.Ink;
            DrawText(ctx, tile.Number.ToString(), c.X, c.Y - s * 0.05, s * 0.3, ink, bold: true);

            double dot = s * 0.03, gap = s * 0.075;
            double x0 = c.X - gap * (tile.Pips - 1) / 2.0;
            for (int i = 0; i < tile.Pips; i++)
                ctx.DrawEllipse(Palette.Brush(ink), null, new Point(x0 + i * gap, c.Y + s * 0.2), dot, dot);
        }

        void DrawPort(DrawingContext ctx, Game game, Port port, (double Scale, double Ox, double Oy) f)
        {
            Hex land = game.Board.IsLand(port.Edge.A) ? port.Edge.A : port.Edge.B;
            var lp = HexLayout.ToPlane(land);
            var ep = HexLayout.ToPlane(port.Edge);
            Point pos = ToPixel((lp.X + (ep.X - lp.X) * 1.75f, lp.Y + (ep.Y - lp.Y) * 1.75f), f);

            Color color = port.IsGeneric ? Color.FromRgb(0xe8, 0xe8, 0xee) : Palette.Resource(port.Resource);
            var pier = new Pen(Palette.Brush(Color.FromRgb(0x9a, 0x7b, 0x4f)), Math.Max(2, f.Scale * 0.05), lineCap: PenLineCap.Round);
            foreach (Vertex v in port.Edge.Endpoints())
                ctx.DrawLine(pier, pos, ToPixel(HexLayout.ToPlane(v), f));

            double r = f.Scale * 0.3;
            ctx.DrawEllipse(Palette.Brush(color), new Pen(Palette.Brush(Palette.Darken(color, 0.5)), 2), pos, r, r);
            string text = port.IsGeneric ? "3:1" : "2:1";
            DrawText(ctx, text, pos.X, pos.Y - f.Scale * 0.06, f.Scale * 0.17, Palette.Ink, bold: true);
            if (!port.IsGeneric)
                DrawText(ctx, port.Resource.ToString(), pos.X, pos.Y + f.Scale * 0.1, f.Scale * 0.1, Palette.Ink, bold: false);
        }

        void DrawRoad(DrawingContext ctx, Edge edge, int owner, (double Scale, double Ox, double Oy) f)
        {
            var ends = new List<Point>();
            foreach (Vertex v in edge.Endpoints()) ends.Add(ToPixel(HexLayout.ToPlane(v), f));

            // Shorten slightly so roads don't smear over the pieces at the corners.
            Point a = Lerp(ends[0], ends[1], 0.12), b = Lerp(ends[0], ends[1], 0.88);
            double grow = Effects?.PieceScale(edge) ?? 1;
            if (grow <= 0) return;
            if (grow < 1)
            {
                // A new road grows out from its middle.
                Point mid = Lerp(a, b, 0.5);
                a = Lerp(mid, a, grow);
                b = Lerp(mid, b, grow);
            }
            Color color = Palette.Player(owner);
            double w = Math.Max(4, f.Scale * 0.11);
            ctx.DrawLine(new Pen(Palette.Brush(Colors.Black), w + 3, lineCap: PenLineCap.Round), a, b);
            ctx.DrawLine(new Pen(Palette.Brush(color), w, lineCap: PenLineCap.Round), a, b);
        }

        void DrawBuilding(DrawingContext ctx, Vertex v, Building b, (double Scale, double Ox, double Oy) f)
        {
            Point p = ToPixel(HexLayout.ToPlane(v), f);
            double pop = Effects?.PieceScale(v) ?? 1;
            if (pop <= 0 && !b.IsCity) return;
            // A new city grows out of the village it replaces, so never shrink it to nothing.
            if (b.IsCity) pop = Math.Max(pop, 0.7);
            double u = f.Scale * (b.IsCity ? 0.2 : 0.15) * pop;
            Color color = Palette.Player(b.Owner);
            var fill = Palette.Brush(color);
            var edge = new Pen(Palette.Brush(Colors.Black), 2, lineJoin: PenLineJoin.Round);

            if (!b.IsCity)
            {
                // House: a square with a pointed roof.
                ctx.DrawGeometry(fill, edge, Polygon(new[]
                {
                    new Point(p.X - u, p.Y + u * 0.9), new Point(p.X - u, p.Y - u * 0.2), new Point(p.X, p.Y - u * 1.3),
                    new Point(p.X + u, p.Y - u * 0.2), new Point(p.X + u, p.Y + u * 0.9),
                }));
            }
            else
            {
                // City: wide base with a taller tower on one side.
                ctx.DrawGeometry(fill, edge, Polygon(new[]
                {
                    new Point(p.X - u * 1.4, p.Y + u * 0.9), new Point(p.X - u * 1.4, p.Y - u * 0.3), new Point(p.X - u * 0.3, p.Y - u * 0.3),
                    new Point(p.X - u * 0.3, p.Y - u * 1.0), new Point(p.X + u * 0.45, p.Y - u * 1.7), new Point(p.X + u * 1.2, p.Y - u * 1.0),
                    new Point(p.X + u * 1.2, p.Y + u * 0.9),
                }));
            }
        }

        void DrawRaider(DrawingContext ctx, Hex hex, (double Scale, double Ox, double Oy) f)
        {
            double s = f.Scale;
            Point c = ToPixel(HexLayout.ToPlane(hex), f);
            var move = Effects?.Raider();
            if (move.HasValue)
            {
                // Hop from the old hex to the new one.
                Point from = ToPixel(HexLayout.ToPlane(move.Value.From), f), to = ToPixel(HexLayout.ToPlane(move.Value.To), f);
                c = Lerp(from, to, move.Value.T);
                c = new Point(c.X, c.Y - move.Value.Lift * s * 0.6);
            }
            c = new Point(c.X + s * 0.35, c.Y + s * 0.3);
            var dark = Palette.Brush(Color.FromRgb(0x16, 0x16, 0x1a));
            var rim = new Pen(Palette.Brush(Color.FromRgb(0xdd, 0xdd, 0xdd)), 1.5);
            ctx.DrawEllipse(dark, rim, new Point(c.X, c.Y - s * 0.16), s * 0.1, s * 0.1);
            ctx.DrawGeometry(dark, rim, Polygon(new[]
            {
                new Point(c.X - s * 0.16, c.Y + s * 0.2), new Point(c.X - s * 0.08, c.Y - s * 0.07),
                new Point(c.X + s * 0.08, c.Y - s * 0.07), new Point(c.X + s * 0.16, c.Y + s * 0.2),
            }));
        }

        void DrawSpots(DrawingContext ctx, (double Scale, double Ox, double Oy) f)
        {
            IReadOnlyList<Spot> spots = _controller.Spots;
            for (int i = 0; i < spots.Count; i++)
            {
                Spot sp = spots[i];
                bool hover = i == _hover;
                Point c = ToPixel(((float)sp.X, (float)sp.Y), f);

                if (sp.Kind == SpotKind.Hex)
                {
                    var pts = new List<Point>();
                    for (int k = 0; k < 6; k++) pts.Add(ToPixel(HexLayout.ToPlane(Vertex.OfCorner(sp.Hex, k)), f));
                    var fill = Palette.Brush(Color.FromArgb((byte)(hover ? 110 : 55), 255, 255, 255));
                    ctx.DrawGeometry(fill, new Pen(Palette.Brush(Colors.White), hover ? 4 : 2), Polygon(pts));
                }
                else
                {
                    double r = f.Scale * (hover ? 0.17 : 0.11);
                    ctx.DrawEllipse(Palette.Brush(Color.FromArgb(hover ? (byte)255 : (byte)220, Palette.Highlight.R, Palette.Highlight.G, Palette.Highlight.B)),
                        new Pen(Palette.Brush(Colors.Black), 1.5), c, r, r);
                }
            }
        }

        // ---- Drawing helpers -----------------------------------------------------------------------

        static Geometry Polygon(IEnumerable<Point> points)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                bool first = true;
                foreach (Point p in points)
                {
                    if (first) g.BeginFigure(p, true);
                    else g.LineTo(p);
                    first = false;
                }
                g.EndFigure(true);
            }
            return geo;
        }

        static Point Lerp(Point a, Point b, double t) => new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        static void DrawText(DrawingContext ctx, string text, double cx, double cy, double size, Color color, bool bold)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                bold ? FontBold : Font, Math.Max(6, size), Palette.Brush(color));
            ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        }

        // ---- Picking -------------------------------------------------------------------------------

        int Pick(Point pixel)
        {
            if (_controller?.Game == null || _controller.Spots.Count == 0) return -1;
            var f = Fit();
            double x = (pixel.X - f.Ox) / f.Scale, y = (pixel.Y - f.Oy) / f.Scale;

            int best = -1;
            double bestSqr = double.MaxValue;
            for (int i = 0; i < _controller.Spots.Count; i++)
            {
                Spot sp = _controller.Spots[i];
                double dx = sp.X - x, dy = sp.Y - y;
                double limit = sp.Kind == SpotKind.Hex ? 0.85 : 0.4;
                double sqr = dx * dx + dy * dy;
                if (sqr <= limit * limit && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>A tooltip for a tile, so the terrain can be read as words as well as pictures.</summary>
        string Describe(Hex? hex)
        {
            if (!hex.HasValue || _controller?.Game == null) return null;
            foreach (Tile t in _controller.Game.Board.Tiles)
            {
                if (t.Hex != hex.Value) continue;
                string what = t.IsWasteland ? "Wasteland: produces nothing" : $"{t.Resource} on {t.Number} ({t.Pips} {(t.Pips == 1 ? "dot" : "dots")})";
                return t.Hex == _controller.Game.RaiderHex ? what + ". The raider is here." : what;
            }
            return null;
        }

        /// <summary>The land tile under a pixel, if any.</summary>
        Hex? TileAt(Point pixel)
        {
            if (_controller?.Game == null || Bounds.Width < 10 || Bounds.Height < 10) return null;
            var f = Fit();
            double x = (pixel.X - f.Ox) / f.Scale, y = (pixel.Y - f.Oy) / f.Scale;
            // The nearest tile center is the hex the point is in; past a corner's reach it's open sea.
            Hex? best = null;
            double bestSqr = 1.0;
            foreach (Tile tile in _controller.Game.Board.Tiles)
            {
                var c = HexLayout.ToPlane(tile.Hex);
                double dx = c.X - x, dy = c.Y - y, sqr = dx * dx + dy * dy;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = tile.Hex;
                }
            }
            return best;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Point at = e.GetPosition(this);
            Hex? tile = TileAt(at);
            if (!Nullable.Equals(tile, _hoverTile))
            {
                _hoverTile = tile;
                ToolTip.SetTip(this, Describe(tile));
                InvalidateVisual();
            }
            int hover = Pick(at);
            if (hover == _hover) return;
            _hover = hover;
            Cursor = new Cursor(hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hover < 0 && !_hoverTile.HasValue) return;
            _hover = -1;
            _hoverTile = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            int pick = Pick(e.GetPosition(this));
            if (pick >= 0) _controller.ClickSpot(_controller.Spots[pick]);
        }
    }
}
