using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Catan.Core;

namespace Catan.Client.Animation
{
    /// <summary>
    /// A row of card stacks with counts: the five resources, optionally followed by a face-down development
    /// pile. Used for the player's hand and for the bank. Stacks bump when a flying card lands on them.
    /// </summary>
    public sealed class CardRow : Control
    {
        const double CardWidth = 38, Gap = 12, BumpTime = 0.35;

        ResourceSet _cards;
        int _dev = -1;
        string _devLabel = "Dev";
        readonly Dictionary<int, double> _bumps = new Dictionary<int, double>();

        public CardRow()
        {
            Height = CardWidth * CardArt.Aspect + 26;
            IsHitTestVisible = false;
        }

        /// <param name="dev">Number of development cards to show as a sixth, face-down pile, or -1 for none.</param>
        public void Show(ResourceSet cards, int dev = -1, string devLabel = "Dev")
        {
            _cards = cards;
            _dev = dev;
            _devLabel = devLabel;
            InvalidateVisual();
        }

        int Slots => _dev >= 0 ? 6 : 5;

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(Slots * CardWidth + (Slots - 1) * Gap + 10, Height);

        /// <summary>Center of a resource's stack (or the development pile for null) in this control's coordinates.</summary>
        public Point SlotCenter(Resource? r)
        {
            int i = r.HasValue ? Array.IndexOf(ResourceSet.Types, r.Value) : (_dev >= 0 ? 5 : 2);
            return new Point(4 + CardWidth / 2 + i * (CardWidth + Gap), 4 + CardWidth * CardArt.Aspect / 2);
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
            for (int i = 0; i < Slots; i++)
            {
                bool isDev = i == 5;
                int count = isDev ? _dev : _cards[ResourceSet.Types[i]];
                Point c = SlotCenter(isDev ? null : ResourceSet.Types[i]);

                double bump = 0;
                if (_bumps.TryGetValue(i, out double at))
                {
                    double t = (now - at) / BumpTime;
                    if (t >= 0 && t < 1) bump = Math.Sin(t * Math.PI) * (1 - t);
                    else if (t >= 1) _bumps.Remove(i);
                }

                double opacity = count > 0 ? 1 : 0.28;
                // A stack: up to three cards peeking out behind the top one.
                int layers = Math.Min(3, Math.Max(0, count - 1));
                for (int k = layers; k >= 1; k--)
                {
                    var p = new Point(c.X + k * 2.2, c.Y - k * 2.2);
                    CardArt.Draw(ctx, p, CardWidth, 0, 1, opacity, faceUp: !isDev, resource: isDev ? null : ResourceSet.Types[i], isDev: isDev);
                }
                var top = new Point(c.X, c.Y - bump * 8);
                CardArt.Draw(ctx, top, CardWidth * (1 + bump * 0.18), 0, 1, opacity, faceUp: !isDev, resource: isDev ? null : ResourceSet.Types[i], isDev: isDev);

                // Count badge.
                var badge = new Point(c.X + CardWidth / 2 - 2, c.Y + CardWidth * CardArt.Aspect / 2 - 4);
                if (count > 0)
                {
                    ctx.DrawEllipse(Palette.Brush(Color.FromRgb(0x14, 0x17, 0x1c)), new Pen(Palette.Brush(Palette.Highlight), 1.5), badge, 10, 10);
                    AnimationLayer.Text(ctx, count.ToString(), badge, 11, Colors.White);
                }

                string label = isDev ? _devLabel : ResourceSet.Types[i].ToString();
                AnimationLayer.Text(ctx, label, new Point(c.X, c.Y + CardWidth * CardArt.Aspect / 2 + 14), 10, Ui.Muted);
            }
        }
    }
}
