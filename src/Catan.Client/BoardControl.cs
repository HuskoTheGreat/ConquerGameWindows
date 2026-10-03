using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Catan.Core;

namespace Catan.Client
{
    /// <summary>
    /// Draws the board in 2D from <see cref="Game"/> state and turns clicks into <see cref="Spot"/> picks.
    /// All geometry comes from <see cref="HexLayout"/>, so the picture and the rules can't disagree.
    /// </summary>
    public sealed class BoardControl : Control
    {
        static readonly double Sqrt3 = Math.Sqrt(3.0);
        static readonly Typeface Font = new Typeface("Segoe UI, Arial, sans-serif");
        static readonly Typeface FontBold = new Typeface("Segoe UI, Arial, sans-serif", FontStyle.Normal, FontWeight.Bold);

        LocalGameController _controller;
        int _hover = -1;

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
            double s = Math.Min(Bounds.Width / (2 * halfW), Bounds.Height / (2 * halfH));
            return (s, Bounds.Width / 2, Bounds.Height / 2);
        }

        Point ToPixel((float X, float Y) p, (double Scale, double Ox, double Oy) f) =>
            new Point(f.Ox + p.X * f.Scale, f.Oy + p.Y * f.Scale);

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
            double s = f.Scale;

            foreach (Port port in game.Board.Ports) DrawPort(ctx, game, port, f);
            foreach (Tile tile in game.Board.Tiles) DrawTile(ctx, tile, f);
            foreach (Tile tile in game.Board.Tiles) DrawToken(ctx, tile, f);

            foreach (var road in game.RoadOwners) DrawRoad(ctx, road.Key, road.Value, f);
            foreach (var b in game.Buildings) DrawBuilding(ctx, b.Key, b.Value, f);

            DrawRobber(ctx, game.RobberHex, f);
            DrawSpots(ctx, f);
        }

        void DrawTile(DrawingContext ctx, Tile tile, (double Scale, double Ox, double Oy) f)
        {
            var center = HexLayout.ToPlane(tile.Hex);
            var points = new List<Point>();
            for (int i = 0; i < 6; i++)
            {
                var c = HexLayout.ToPlane(Vertex.OfCorner(tile.Hex, i));
                // Shrink a touch so neighbors show a thin seam.
                points.Add(ToPixel((center.X + (c.X - center.X) * 0.965f, center.Y + (c.Y - center.Y) * 0.965f), f));
            }

            Color fill = Palette.Resource(tile.Resource);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(fill, 0), new GradientStop(Palette.Darken(fill, 0.82), 1) },
            };
            ctx.DrawGeometry(brush, new Pen(Palette.Brush(Palette.Darken(fill, 0.55)), Math.Max(1.5, f.Scale * 0.04)), Polygon(points));

            Point c0 = ToPixel(center, f);
            string label = tile.IsDesert ? "Desert" : tile.Resource.ToString();
            DrawText(ctx, label, c0.X, c0.Y - f.Scale * 0.55, f.Scale * 0.17, Color.FromArgb(200, 255, 255, 255), bold: false);
        }

        void DrawToken(DrawingContext ctx, Tile tile, (double Scale, double Ox, double Oy) f)
        {
            if (tile.IsDesert) return;
            double s = f.Scale;
            Point c = ToPixel(HexLayout.ToPlane(tile.Hex), f);
            ctx.DrawEllipse(Palette.Brush(Palette.Token), new Pen(Palette.Brush(Palette.Darken(Palette.Token, 0.6)), 1.5), c, s * 0.33, s * 0.33);

            Color ink = tile.Number == 6 || tile.Number == 8 ? Palette.Hot : Palette.Ink;
            DrawText(ctx, tile.Number.ToString(), c.X, c.Y - s * 0.06, s * 0.27, ink, bold: true);

            double dot = s * 0.028, gap = s * 0.072;
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
            Color color = Palette.Player(owner);
            double w = Math.Max(4, f.Scale * 0.11);
            ctx.DrawLine(new Pen(Palette.Brush(Colors.Black), w + 3, lineCap: PenLineCap.Round), a, b);
            ctx.DrawLine(new Pen(Palette.Brush(color), w, lineCap: PenLineCap.Round), a, b);
        }

        void DrawBuilding(DrawingContext ctx, Vertex v, Building b, (double Scale, double Ox, double Oy) f)
        {
            Point p = ToPixel(HexLayout.ToPlane(v), f);
            double u = f.Scale * (b.IsCity ? 0.2 : 0.15);
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

        void DrawRobber(DrawingContext ctx, Hex hex, (double Scale, double Ox, double Oy) f)
        {
            double s = f.Scale;
            Point c = ToPixel(HexLayout.ToPlane(hex), f);
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

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            int hover = Pick(e.GetPosition(this));
            if (hover == _hover) return;
            _hover = hover;
            Cursor = new Cursor(hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hover < 0) return;
            _hover = -1;
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
