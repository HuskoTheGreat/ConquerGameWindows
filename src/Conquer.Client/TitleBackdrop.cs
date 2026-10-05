using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Conquer.Client.Animation;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// The title screen's backdrop: an endless field of island tiles in the resource colors, tilted and drifting
    /// slowly, with a vignette that keeps the middle dark enough for the wordmark and menu. Purely decorative.
    /// It only animates while visible and while animations are switched on; otherwise it holds a still frame.
    /// </summary>
    public sealed class TitleBackdrop : Control
    {
        const double Size = 72;          // hex corner radius in pixels
        const double Tilt = -0.21;       // radians
        static readonly double Sqrt3 = Math.Sqrt(3.0);
        static readonly Vector Drift = new Vector(11, 5); // pixels per second, in the tilted frame

        static readonly Color Top = Color.FromRgb(0x6c, 0xcc, 0xf4);
        static readonly Color Bottom = Color.FromRgb(0x2a, 0x8c, 0xd0);

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Geometry _hex = HexGeometry(Size * 0.97);
        bool _framePending;

        static TitleBackdrop()
        {
            AffectsRender<TitleBackdrop>(IsVisibleProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<TitleBackdrop>(false);
        }

        /// <summary>Seconds of drift to draw; null follows the real clock.</summary>
        public double? FixedTime { get; set; }

        double Seconds => FixedTime ?? (AnimationLayer.Enabled ? _clock.Elapsed.TotalSeconds : 0);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Tick();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible) Tick();
        }

        void Tick()
        {
            if (_framePending || !IsVisible || !AnimationLayer.Enabled) return;
            TopLevel top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            _framePending = true;
            top.RequestAnimationFrame(_ =>
            {
                _framePending = false;
                InvalidateVisual();
                Tick();
            });
        }

        public override void Render(DrawingContext ctx)
        {
            var bounds = new Rect(Bounds.Size);
            ctx.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Top, 0), new GradientStop(Bottom, 1) },
            }, null, bounds);
            if (bounds.Width < 1 || bounds.Height < 1) return;

            double t = Seconds;
            Point center = bounds.Center;
            double reach = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2 + Size * 2;
            Vector drift = Drift * t;

            // The lattice point under the screen's center, in tile units.
            double ux = -drift.X, uy = -drift.Y;
            double rowH = Size * 1.5, colW = Size * Sqrt3;

            var tilt = Matrix.CreateRotation(Tilt) * Matrix.CreateTranslation(center.X, center.Y);
            using (ctx.PushTransform(tilt))
            {
                int r0 = (int)Math.Floor((uy - reach) / rowH), r1 = (int)Math.Ceiling((uy + reach) / rowH);
                for (int r = r0; r <= r1; r++)
                {
                    int q0 = (int)Math.Floor((ux - reach) / colW - r / 2.0), q1 = (int)Math.Ceiling((ux + reach) / colW - r / 2.0);
                    for (int q = q0; q <= q1; q++)
                    {
                        double x = colW * (q + r / 2.0) + drift.X, y = rowH * r + drift.Y;
                        DrawTile(ctx, q, r, new Point(x, y), t);
                    }
                }
            }

            // Keep the middle calm and dark so the title reads, and fade the edges into the window.
            ctx.DrawRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x90, 0xd8, 0xf2, 0xff), 0),
                    new GradientStop(Color.FromArgb(0x50, 0xb0, 0xe2, 0xff), 0.55),
                    new GradientStop(Color.FromArgb(0x00, 0x60, 0xb8, 0xf0), 0.85),
                    new GradientStop(Color.FromArgb(0x40, 0x10, 0x50, 0x90), 1.1),
                },
            }, null, bounds);
        }

        static readonly IPen Wave = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, 0.45), 3, null, PenLineCap.Round);
        static readonly IBrush Sand = new ImmutableSolidColorBrush(Color.FromRgb(0xf3, 0xdc, 0x9c));
        static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x5a, 0x3e, 0x22), 0.85), 3.5 / (Size * 0.86), null, PenLineCap.Round, PenLineJoin.Round);

        void DrawTile(DrawingContext ctx, int q, int r, Point at, double t)
        {
            uint h = Hash(q, r);
            int kind = (int)(h % 20);
            using (ctx.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
            {
                if (kind >= 16)
                {
                    // Open sea between the islands: a couple of cartoon waves.
                    for (int i = -1; i <= 1; i += 2)
                    {
                        double y = i * Size * 0.3, x = i * Size * 0.2, w = Size * 0.18;
                        var g = new StreamGeometry();
                        using (StreamGeometryContext c = g.Open())
                        {
                            c.BeginFigure(new Point(x - w, y), false);
                            c.QuadraticBezierTo(new Point(x - w / 2, y - w * 0.7), new Point(x, y));
                            c.QuadraticBezierTo(new Point(x + w / 2, y - w * 0.7), new Point(x + w, y));
                            c.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, Wave, g);
                    }
                    return;
                }
                Resource res = kind < 14 ? ResourceSet.Types[kind % 5] : Resource.Wasteland;
                // Each tile bobs gently on its own slow cycle.
                double phase = (h >> 8) % 628 / 100.0;
                double bob = Math.Sin(t * 0.8 + phase) * 2.5;
                using (ctx.PushTransform(Matrix.CreateTranslation(0, bob)))
                {
                    ctx.DrawGeometry(Sand, null, _hex);
                    TileArt.DrawHex(ctx, res, (int)((h >> 5) % TileArt.Variants), new Point(0, 0), Size * 0.86);
                    using (ctx.PushTransform(Matrix.CreateScale(Size * 0.86, Size * 0.86)))
                        ctx.DrawGeometry(null, Outline, TileArt.UnitHex);
                }
            }
        }

        static uint Hash(int q, int r)
        {
            unchecked
            {
                uint h = (uint)q * 0x9E3779B1u ^ (uint)r * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        static Geometry HexGeometry(double radius)
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
    }
}
