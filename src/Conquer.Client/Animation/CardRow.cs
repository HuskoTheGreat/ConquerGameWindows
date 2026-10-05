using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client.Animation
{
    /// <summary>
    /// A row of card stacks with counts: the five resources, optionally followed by a face-down action
    /// pile. Used for the player's hand (spread as a fan) and for the bank. Stacks bump when a flying card
    /// lands on them.
    /// </summary>
    public sealed class CardRow : Control
    {
        const double BumpTime = 0.35, LabelSpace = 22;

        double _cardWidth = 38, _gap = 12;
        ResourceSet _cards;
        int _dev = -1;
        string _devLabel = "Action";
        readonly Dictionary<int, double> _bumps = new Dictionary<int, double>();

        public CardRow()
        {
            IsHitTestVisible = false;
            UpdateHeight();
        }

        /// <summary>Spread the stacks in a gentle arc, like a hand of cards.</summary>
        public bool Fan { get; set; }

        /// <summary>Colour of the names under the stacks.</summary>
        public Color LabelColor { get; set; } = Ui.Muted;

        public double CardWidth => _cardWidth;

        /// <summary>Names under the stacks with the counts on badges; without labels (the bank) the count goes under the stack instead.</summary>
        public bool ShowLabels { get; set; } = true;

        /// <summary>Resizes the cards (the window scales them with its width).</summary>
        public void SetCardSize(double width, double gap)
        {
            if (Math.Abs(width - _cardWidth) < 0.5 && Math.Abs(gap - _gap) < 0.5) return;
            _cardWidth = width;
            _gap = gap;
            UpdateHeight();
            InvalidateMeasure();
            InvalidateVisual();
        }

        void UpdateHeight() => Height = 10 + _cardWidth * CardArt.Aspect + (ShowLabels ? LabelSpace : 18) + (Fan ? _cardWidth * 0.18 : 0);

        /// <param name="dev">Number of action cards to show as a sixth, face-down pile, or -1 for none.</param>
        public void Show(ResourceSet cards, int dev = -1, string devLabel = "Action")
        {
            _cards = cards;
            _dev = dev;
            _devLabel = devLabel;
            UpdateHeight();
            InvalidateVisual();
        }

        int Slots => _dev >= 0 ? 6 : 5;

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(Slots * _cardWidth + (Slots - 1) * _gap + 14, Height);

        /// <summary>Tilt of a slot in the fan, in radians.</summary>
        double Tilt(int i) => Fan ? (i - (Slots - 1) / 2.0) * 0.07 : 0;

        double Arc(int i)
        {
            if (!Fan) return 0;
            double k = (i - (Slots - 1) / 2.0) / ((Slots - 1) / 2.0);
            return k * k * _cardWidth * 0.18;
        }

        /// <summary>Center of a resource's stack (or the action pile for null) in this control's coordinates.</summary>
        public Point SlotCenter(Resource? r)
        {
            int i = r.HasValue ? Array.IndexOf(ResourceSet.Types, r.Value) : (_dev >= 0 ? 5 : 2);
            return new Point(6 + _cardWidth / 2 + i * (_cardWidth + _gap), 8 + _cardWidth * CardArt.Aspect / 2 + Arc(i));
        }

        public void Bump(Resource? r)
        {
            int i = r.HasValue ? Array.IndexOf(ResourceSet.Types, r.Value) : 5;
            _bumps[i] = AnimationLayer.Clock();
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double now = AnimationLayer.Clock();
            double w = _cardWidth, h = w * CardArt.Aspect;
            for (int i = 0; i < Slots; i++)
            {
                bool isDev = i == 5;
                int count = isDev ? _dev : _cards[ResourceSet.Types[i]];
                Point c = SlotCenter(isDev ? null : ResourceSet.Types[i]);
                double tilt = Tilt(i);

                double bump = 0;
                if (_bumps.TryGetValue(i, out double at))
                {
                    double t = (now - at) / BumpTime;
                    if (t >= 0 && t < 1) bump = Math.Sin(t * Math.PI) * (1 - t);
                    else if (t >= 1) _bumps.Remove(i);
                }

                double opacity = count > 0 ? 1 : 0.3;
                // A stack: up to three cards peeking out behind the top one.
                int layers = Math.Min(3, Math.Max(0, count - 1));
                double step = Math.Max(1.6, w * 0.055);
                for (int k = layers; k >= 1; k--)
                {
                    var p = new Point(c.X + k * step, c.Y - k * step);
                    CardArt.Draw(ctx, p, w, tilt, 1, opacity, faceUp: !isDev, resource: isDev ? null : ResourceSet.Types[i], isDev: isDev);
                }
                var top = new Point(c.X, c.Y - bump * 8);
                CardArt.Draw(ctx, top, w * (1 + bump * 0.18), tilt, 1, opacity, faceUp: !isDev, resource: isDev ? null : ResourceSet.Types[i], isDev: isDev);

                // Count badge: a bright red bubble, like a notification.
                double br = Math.Max(9, w * 0.22);
                var badge = new Point(c.X + w / 2 - br * 0.35, c.Y - h / 2 + br * 0.35);
                if (!ShowLabels)
                {
                    AnimationLayer.Text(ctx, Math.Max(0, count).ToString(), new Point(c.X, c.Y + h / 2 + 10), Math.Max(11, Math.Min(14, w * 0.42)), count > 0 ? Palette.Text : Ui.Muted);
                    continue;
                }
                if (count > 0)
                {
                    ctx.DrawEllipse(Palette.Brush(Color.FromArgb(70, 0, 0, 0)), null, new Point(badge.X, badge.Y + 1.5), br, br);
                    ctx.DrawEllipse(Palette.Brush(Color.FromRgb(0xe8, 0x3a, 0x30)), new Pen(Palette.Brush(Colors.White), Math.Max(1.5, br * 0.18)), badge, br, br);
                    AnimationLayer.Text(ctx, count.ToString(), badge, br * 1.15, Colors.White);
                }

                string label = isDev ? _devLabel : ResourceSet.Types[i].ToString();
                AnimationLayer.Text(ctx, label, new Point(c.X, c.Y + h / 2 + 11), Math.Max(10, Math.Min(13, w * 0.24)), LabelColor);
            }
        }
    }
}
