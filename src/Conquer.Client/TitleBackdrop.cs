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
        const double Size = 56;          // hex corner radius in pixels
        const double Tilt = -0.21;       // radians
        static readonly double Sqrt3 = Math.Sqrt(3.0);
        static readonly Vector Drift = new Vector(11, 5); // pixels per second, in the tilted frame

        static readonly Color Top = Color.FromRgb(0x0d, 0x16, 0x26);
        static readonly Color Bottom = Color.FromRgb(0x07, 0x0a, 0x10);

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Geometry _hex = HexGeometry(Size * 0.94);
        readonly Geometry _hexInner = HexGeometry(Size * 0.80);
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
                    new GradientStop(Color.FromArgb(0xe6, 0x0a, 0x0e, 0x16), 0),
                    new GradientStop(Color.FromArgb(0xb0, 0x0a, 0x0e, 0x16), 0.5),
                    new GradientStop(Color.FromArgb(0x68, 0x0a, 0x0e, 0x16), 0.8),
                    new GradientStop(Color.FromArgb(0xc8, 0x05, 0x07, 0x0b), 1.1),
                },
            }, null, bounds);
        }

        void DrawTile(DrawingContext ctx, int q, int r, Point at, double t)
        {
            uint h = Hash(q, r);
            int kind = (int)(h % 20);
            if (kind >= 17) return; // open sea between islands
            Resource res = kind < 15 ? ResourceSet.Types[kind % 5] : Resource.Wasteland;
            Color baseColor = Palette.Resource(res);

            // Each tile breathes on its own slow cycle.
            double phase = (h >> 8) % 628 / 100.0;
            double glow = 0.5 + 0.5 * Math.Sin(t * 0.45 + phase);
            double shade = 0.44 + 0.14 * glow;
            Color fill = Palette.Darken(baseColor, shade);

            using (ctx.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
            {
                ctx.DrawGeometry(new ImmutableSolidColorBrush(fill, 0.92), new ImmutablePen(new ImmutableSolidColorBrush(Palette.Darken(baseColor, 0.28)), 2), _hex);
                ctx.DrawGeometry(null, new ImmutablePen(new ImmutableSolidColorBrush(Palette.Darken(baseColor, 0.75), 0.25 + 0.2 * glow), 1.2), _hexInner);
                if (res != Resource.Wasteland && (h >> 4) % 3 == 0)
                    ctx.DrawEllipse(new ImmutableSolidColorBrush(Palette.Token, 0.16 + 0.08 * glow), null, new Point(0, 0), Size * 0.26, Size * 0.26);
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
                    double a = Math.PI / 180 * (60 * i - 30);
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
