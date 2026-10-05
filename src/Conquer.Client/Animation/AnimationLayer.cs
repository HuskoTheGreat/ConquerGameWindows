using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client.Animation
{
    /// <summary>
    /// A see-through layer over the whole window that plays <see cref="VisualEvent"/>s: dice, flying cards,
    /// action-card pulls, turn banners and the victory screen. It runs on its own clock and never
    /// blocks or delays the game, so it behaves the same whether state comes from the local engine or from
    /// a server. Where things fly to and from is asked of the window through <see cref="Resolve"/>.
    /// </summary>
    public sealed class AnimationLayer : Control
    {
        /// <summary>Master switch (the setup dialog's "Animations" box). Off means state changes just appear.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Seconds since some fixed moment. Tests swap this for a hand-cranked clock.</summary>
        public static Func<double> Clock { get; set; } = DefaultClock;

        static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static double DefaultClock() => Watch.Elapsed.TotalSeconds;

        static readonly Typeface Font = new Typeface(Ui.Font, FontStyle.Normal, FontWeight.Black);

        readonly List<Fx> _effects = new List<Fx>();
        bool _framePending;

        public AnimationLayer()
        {
            IsHitTestVisible = false;
            ClipToBounds = false;
            Board.Clock = () => Clock();
        }

        /// <summary>Animation speed multiplier; 2 plays everything twice as fast.</summary>
        public double Speed { get; set; } = 1;

        /// <summary>Position, in this layer's coordinates, of a place a card can fly to or from.</summary>
        public Func<Place, Resource?, Point?> Resolve { get; set; } = (_, _) => null;

        /// <summary>The board's rectangle in this layer's coordinates (dice and banners are centered on it).</summary>
        public Func<Rect> BoardArea { get; set; }

        public Func<int, string> NameOf { get; set; } = id => $"Player {id + 1}";

        /// <summary>Effects the board itself draws (piece pops, raider slide, tile glow).</summary>
        public BoardEffects Board { get; } = new BoardEffects();

        /// <summary>Other controls to repaint on every animation frame (the board, the hand).</summary>
        public List<Control> Followers { get; } = new List<Control>();

        /// <summary>Raised when a flying card arrives, so the place it lands can react.</summary>
        public event Action<Place, Resource?> Landed;

        public bool Busy
        {
            get
            {
                double now = Now;
                return _effects.Any(e => now < e.Start + e.Duration) || Board.Busy;
            }
        }

        double Now => Clock();

        /// <summary>Drops every running animation, e.g. when a new game starts.</summary>
        public void Clear()
        {
            _effects.Clear();
            Board.Clear();
            InvalidateVisual();
        }

        // ---- Scheduling ----------------------------------------------------------------------------

        /// <summary>Schedules animations for a batch of events found by one state change.</summary>
        public void Play(IReadOnlyList<VisualEvent> events)
        {
            if (!Enabled || events.Count == 0) return;
            Board.Speed = Speed;
            double t0 = Now, s = 1 / Speed;

            // Everything caused by a roll waits for the dice to land.
            double after = 0;
            foreach (VisualEvent e in events)
            {
                if (e is DiceRolled) after = 0.95 * s;
            }

            int card = 0, piece = 0;
            foreach (VisualEvent e in events)
            {
                switch (e)
                {
                    case TurnStarted ts:
                        Add(new Banner(this, t0, 1.6 * s, $"{NameOf(ts.Player)}'s turn", Palette.Player(ts.Player)));
                        break;

                    case DiceRolled dr:
                        Add(new Dice(this, t0, 1.9 * s, dr.Total));
                        Board.Glow(dr.Total, t0 + 0.85 * s);
                        break;

                    case ActionCardPlayed dp:
                        Add(new PlayedCard(this, t0 + after, 1.7 * s, dp.Player, dp.Card));
                        after += 0.5 * s;
                        break;

                    case RaiderMoved rm:
                        Board.MoveRaider(rm.From, rm.To, t0 + after);
                        break;

                    case VillagePlaced sp:
                        Board.Pop(sp.At, t0 + after + piece++ * 0.08 * s);
                        break;
                    case CityPlaced cp:
                        Board.Pop(cp.At, t0 + after + piece++ * 0.08 * s);
                        break;
                    case RoadPlaced rp:
                        Board.Pop(rp.At, t0 + after + piece++ * 0.08 * s);
                        break;

                    case CardsMoved cm:
                        for (int i = 0; i < cm.Count; i++)
                        {
                            // Stagger cards so a stack reads as a stream, but keep big transfers brisk.
                            double start = t0 + after + Math.Min(card++ * 0.075, 1.2) * s;
                            Add(new Flight(this, start, 0.62 * s, cm.From, cm.To, cm.Resource));
                        }
                        break;

                    case ActionCardDrawn dd:
                        Add(new CardPull(this, t0 + after + card++ * 0.075 * s, 1.7 * s, dd.Player, dd.Card));
                        break;

                    case AwardTaken at:
                        string what = at.Award == Award.GreatRoad ? "Great Road" : "Grand Army";
                        Add(new Banner(this, t0 + after + 0.3 * s, 2.2 * s, $"{what}: {NameOf(at.Player)}", Palette.Player(at.Player), low: true));
                        break;

                    case RulesChanged rc:
                        Add(new RulesCard(this, t0, (2.6 + 0.4 * Math.Min(rc.Changes.Count, 6)) * s, rc.Changes));
                        break;

                    case GameWon gw:
                        Add(new Victory(this, t0 + after + 0.4 * s, 7 * s, gw.Player));
                        break;
                }
            }
            RequestFrame();
        }

        void Add(Fx e) => _effects.Add(e);

        // ---- Frame loop ----------------------------------------------------------------------------

        void RequestFrame()
        {
            InvalidateVisual();
            foreach (Control c in Followers) c.InvalidateVisual();
            if (_framePending) return;
            TopLevel top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            _framePending = true;
            top.RequestAnimationFrame(_ =>
            {
                _framePending = false;
                if (Busy) RequestFrame();
                else
                {
                    InvalidateVisual();
                    foreach (Control c in Followers) c.InvalidateVisual();
                }
            });
        }

        /// <summary>Repaints everything for the current clock (tests call this after moving the clock).</summary>
        public void Tick() => RequestFrame();

        public override void Render(DrawingContext ctx)
        {
            double now = Now;
            for (int i = 0; i < _effects.Count; i++)
            {
                Fx e = _effects[i];
                double t = (now - e.Start) / e.Duration;
                if (t >= 1)
                {
                    e.Finish();
                    _effects.RemoveAt(i--);
                    continue;
                }
                if (t >= 0) e.Draw(ctx, t);
            }
        }

        // ---- Geometry helpers for effects -----------------------------------------------------------

        internal Rect Area => BoardArea?.Invoke() ?? new Rect(Bounds.Size);

        internal Point Where(Place p, Resource? r, ref Point? cache)
        {
            if (p.Kind == PlaceKind.Table) return Area.Center;
            Point? at = Resolve(p, r);
            if (at.HasValue) cache = at;
            return cache ?? Area.Center;
        }

        // Raised from inside Render, where repainting isn't allowed, so listeners run just after the frame.
        internal void RaiseLanded(Place p, Resource? r) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Landed?.Invoke(p, r);
            RequestFrame();
        });

        internal static void Text(DrawingContext ctx, string text, Point center, double size, Color color)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Palette.Brush(color));
            ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        }

        internal static FormattedText Measure(string text, double size) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Brushes.White);

        internal static Point Lerp(Point a, Point b, double t) => new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>A point along a curved throw from a to b that bulges upward by <paramref name="lift"/>.</summary>
        internal static Point Arc(Point a, Point b, double t, double lift)
        {
            Point mid = Lerp(a, b, 0.5);
            var control = new Point(mid.X, mid.Y - lift);
            double u = 1 - t;
            return new Point(u * u * a.X + 2 * u * t * control.X + t * t * b.X, u * u * a.Y + 2 * u * t * control.Y + t * t * b.Y);
        }

        // =============================================================================================
        // Effects
        // =============================================================================================

        abstract class Fx
        {
            protected readonly AnimationLayer L;
            public double Start { get; }
            public double Duration { get; }

            protected Fx(AnimationLayer layer, double start, double duration)
            {
                L = layer;
                Start = start;
                Duration = Math.Max(0.01, duration);
            }

            /// <param name="t">Progress through the effect, 0 to 1.</param>
            public abstract void Draw(DrawingContext ctx, double t);

            public virtual void Finish() { }
        }

        /// <summary>One card thrown from one place to another along an arc.</summary>
        sealed class Flight : Fx
        {
            readonly Place _from, _to;
            readonly Resource? _kind;
            readonly double _spin;
            Point? _a, _b;
            bool _landed;

            public Flight(AnimationLayer layer, double start, double duration, Place from, Place to, Resource? kind) : base(layer, start, duration)
            {
                _from = from;
                _to = to;
                _kind = kind;
                _spin = (new Random(HashCode.Combine(start, from, to)).NextDouble() - 0.5) * 0.9;
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Point a = L.Where(_from, _kind, ref _a), b = L.Where(_to, _kind, ref _b);
                double e = Ease.InOutCubic(t);
                double dist = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                Point p = Arc(a, b, e, Math.Min(140, dist * 0.35));

                // Cards from a tile or the bank grow out of it; every card shrinks a little as it lands.
                double grow = _from.Kind == PlaceKind.Player ? 1 : Ease.BackOut(Ease.Span(t, 0, 0.25));
                double size = 34 * grow * (1 + 0.25 * Math.Sin(e * Math.PI)) * (1 - 0.25 * Ease.Span(t, 0.8, 1));
                double rot = _spin * Math.Sin(e * Math.PI);
                double fade = 1 - Ease.Span(t, 0.9, 1);
                CardArt.Draw(ctx, p, size, rot, 1, fade, faceUp: _kind.HasValue, resource: _kind);

                if (t > 0.97) Finish();
            }

            public override void Finish()
            {
                if (_landed) return;
                _landed = true;
                L.RaiseLanded(_to, _kind);
            }
        }

        /// <summary>
        /// The card pull: an action card lifts off the deck, grows toward the middle of the board, flips
        /// over to show what it is (to its owner), then drops into its owner's hand.
        /// </summary>
        sealed class CardPull : Fx
        {
            readonly int _player;
            readonly ActionCard? _card;
            Point? _deck, _hand;
            bool _landed;

            public CardPull(AnimationLayer layer, double start, double duration, int player, ActionCard? card) : base(layer, start, duration)
            {
                _player = player;
                _card = card;
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Point deck = L.Where(Place.DevDeck, null, ref _deck);
                Point hand = L.Where(Place.Seat(_player), null, ref _hand);
                Point stage = new Point(L.Area.Center.X, L.Area.Center.Y - 20);

                Point p;
                double size, rot = 0, flip = 1;
                bool faceUp = false;

                if (t < 0.3)
                {
                    // Lift off the deck and rise to the stage, turning slightly.
                    double u = Ease.OutCubic(t / 0.3);
                    p = Arc(deck, stage, u, 60);
                    size = 34 + (110 - 34) * u;
                    rot = -0.25 * Math.Sin(u * Math.PI);
                }
                else if (t < 0.75)
                {
                    p = stage;
                    size = 110;
                    if (_card.HasValue)
                    {
                        // Flip: squash to edge-on, swap faces, open back up.
                        double u = Ease.Span(t, 0.33, 0.55);
                        flip = Math.Cos(u * Math.PI);
                        faceUp = u > 0.5;
                    }
                    else
                    {
                        // Someone else's card: a little wobble, but it stays face down.
                        rot = 0.05 * Math.Sin((t - 0.3) * 40) * (1 - Ease.Span(t, 0.3, 0.6));
                    }
                    DrawGlow(ctx, p, 120 * (1 - Ease.Span(t, 0.6, 0.75)) + 20);
                }
                else
                {
                    double u = Ease.InOutCubic((t - 0.75) / 0.25);
                    p = Arc(stage, hand, u, 40);
                    size = 110 + (30 - 110) * u;
                    faceUp = _card.HasValue;
                    if (t > 0.97 && !_landed)
                    {
                        _landed = true;
                        L.RaiseLanded(Place.Seat(_player), null);
                    }
                }

                CardArt.Draw(ctx, p, size, rot, flip, 1 - Ease.Span(t, 0.95, 1), faceUp, dev: _card, isDev: true);
            }

            static void DrawGlow(DrawingContext ctx, Point c, double r)
            {
                var glow = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.FromArgb(190, 0xff, 0xe0, 0x8a), 0), new GradientStop(Color.FromArgb(0, 0xff, 0xe0, 0x8a), 1) },
                };
                ctx.DrawEllipse(glow, null, c, r * 1.3, r * 1.3);
            }
        }

        /// <summary>An action card shown face up to everyone as it's played.</summary>
        sealed class PlayedCard : Fx
        {
            readonly int _player;
            readonly ActionCard? _card;
            Point? _from;

            public PlayedCard(AnimationLayer layer, double start, double duration, int player, ActionCard? card) : base(layer, start, duration)
            {
                _player = player;
                _card = card;
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Point from = L.Where(Place.Seat(_player), null, ref _from);
                Point stage = new Point(L.Area.Center.X, L.Area.Center.Y - 20);
                double u = Ease.OutCubic(Ease.Span(t, 0, 0.3));
                Point p = Arc(from, stage, u, 50);
                double size = 30 + 80 * u;
                double f = Ease.Span(t, 0.28, 0.48);
                double flip = Math.Cos(f * Math.PI);
                double fade = 1 - Ease.Span(t, 0.82, 1);
                double scale = 1 + 0.15 * Ease.Span(t, 0.82, 1);
                CardArt.Draw(ctx, p, size * scale, 0, flip, fade, faceUp: f > 0.5, dev: _card, isDev: true);

                if (t > 0.45)
                {
                    string title = _card.HasValue ? Pretty(_card.Value) : "Action card";
                    double a = Ease.Span(t, 0.45, 0.55) * fade;
                    using (ctx.PushOpacity(a))
                        Text(ctx, $"{L.NameOf(_player)} plays {title}", new Point(stage.X, stage.Y + size * CardArt.Aspect / 2 + 22), 18, Colors.White);
                }
            }

            static string Pretty(ActionCard c) => c switch
            {
                ActionCard.VictoryPoint => "a Victory Point",
                ActionCard.Engineers => "Engineers",
                ActionCard.Harvest => "Harvest",
                _ => c.ToString(),
            };
        }

        /// <summary>Two dice tumble onto the board, settle on the roll, and show the total.</summary>
        sealed class Dice : Fx
        {
            readonly int _total, _d1, _d2;
            readonly Random _rng;

            public Dice(AnimationLayer layer, double start, double duration, int total) : base(layer, start, duration)
            {
                _total = total;
                _rng = new Random(HashCode.Combine(start, total));
                // Any split that adds up works; pick one at random so the same total doesn't always look alike.
                int lo = Math.Max(1, total - 6), hi = Math.Min(6, total - 1);
                _d1 = _rng.Next(lo, hi + 1);
                _d2 = total - _d1;
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Rect area = L.Area;
                double size = Math.Clamp(Math.Min(area.Width, area.Height) * 0.11, 44, 76);
                var rest = area.Center;
                double fade = 1 - Ease.Span(t, 0.85, 1);

                const double land = 0.48; // fraction of the effect spent tumbling
                double u = Ease.Span(t, 0, land);
                bool settled = t >= land;

                using (ctx.PushOpacity(fade))
                {
                    for (int k = 0; k < 2; k++)
                    {
                        double side = k == 0 ? -1 : 1;
                        var target = new Point(rest.X + side * size * 0.68, rest.Y + (k == 0 ? -size * 0.08 : size * 0.1));
                        var origin = new Point(area.X - size, area.Bottom - size * (1.4 + k * 0.5));

                        // Slide in from the lower left with two decaying bounces.
                        double x = origin.X + (target.X - origin.X) * Ease.OutCubic(u);
                        double bounce = Math.Abs(Math.Sin(u * Math.PI * 2.5)) * (1 - u) * size * 1.4;
                        double y = origin.Y + (target.Y - origin.Y) * Ease.OutCubic(u) - bounce;
                        double rot = settled ? (k == 0 ? -0.12 : 0.09) : (1 - u) * (8 + k * 3) + (k == 0 ? -0.12 : 0.09);

                        int face = settled ? (k == 0 ? _d1 : _d2) : 1 + (int)((u * 23 + k * 3) % 6);
                        DrawDie(ctx, new Point(x, y), size, rot, face);
                    }

                    if (settled)
                    {
                        double a = Ease.Span(t, land, land + 0.08);
                        double pop = Ease.BackOut(Ease.Span(t, land, land + 0.12));
                        var pill = new Point(rest.X, rest.Y + size * 1.05);
                        bool seven = _total == 7;
                        Color bg = seven ? Palette.Hot : Color.FromRgb(0x14, 0x17, 0x1c);
                        using (ctx.PushOpacity(a))
                        using (ctx.PushTransform(Matrix.CreateTranslation(-pill.X, -pill.Y) * Matrix.CreateScale(pop, pop) * Matrix.CreateTranslation(pill.X, pill.Y)))
                        {
                            ctx.DrawRectangle(Palette.Brush(Color.FromArgb(225, bg.R, bg.G, bg.B)), new Pen(Palette.Brush(Palette.Highlight), 2),
                                new Rect(pill.X - 34, pill.Y - 20, 68, 40), 20, 20);
                            Text(ctx, _total.ToString(), pill, 24, Palette.Highlight);
                        }
                        if (seven)
                        {
                            using (ctx.PushOpacity(a))
                                Text(ctx, "Raider!", new Point(pill.X, pill.Y + 38), 18, Color.FromRgb(0xff, 0x9a, 0x8c));
                        }
                    }
                }
            }

            static void DrawDie(DrawingContext ctx, Point c, double size, double rot, int face)
            {
                using (ctx.PushTransform(Matrix.CreateRotation(rot) * Matrix.CreateTranslation(c.X, c.Y)))
                {
                    var rect = new Rect(-size / 2, -size / 2, size, size);
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(80, 0, 0, 0)), null, rect.Translate(new Vector(size * 0.06, size * 0.09)), size * 0.18, size * 0.18);
                    var fill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromRgb(0xff, 0xfd, 0xf6), 0), new GradientStop(Color.FromRgb(0xe2, 0xd9, 0xc4), 1) },
                    };
                    ctx.DrawRectangle(fill, new Pen(Palette.Brush(Color.FromRgb(0x8a, 0x80, 0x6c)), 1.5), rect, size * 0.18, size * 0.18);

                    double q = size * 0.26, r = size * 0.085;
                    var pip = Palette.Brush(face == 1 ? Palette.Hot : Palette.Ink);
                    void P(double x, double y) => ctx.DrawEllipse(pip, null, new Point(x * q, y * q), r * (face == 1 ? 1.4 : 1), r * (face == 1 ? 1.4 : 1));
                    if (face % 2 == 1) P(0, 0);
                    if (face >= 2) { P(-1, -1); P(1, 1); }
                    if (face >= 4) { P(1, -1); P(-1, 1); }
                    if (face == 6) { P(-1, 0); P(1, 0); }
                }
            }
        }

        /// <summary>A ribbon that slides in over the board ("Blue's turn", "Great Road: Red").</summary>
        sealed class Banner : Fx
        {
            readonly string _text;
            readonly Color _color;
            readonly bool _low;

            public Banner(AnimationLayer layer, double start, double duration, string text, Color color, bool low = false) : base(layer, start, duration)
            {
                _text = text;
                _color = color;
                _low = low;
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Rect area = L.Area;
                double inT = Ease.OutCubic(Ease.Span(t, 0, 0.18));
                double outT = Ease.Span(t, 0.8, 1);
                double y = _low ? area.Bottom - 70 + 30 * (1 - inT) : area.Y + 46 - 30 * (1 - inT) - 20 * outT;
                var ft = Measure(_text, 22);
                double w = ft.Width + 64, h = 44;
                var rect = new Rect(area.Center.X - w / 2, y - h / 2, w, h);

                using (ctx.PushOpacity(inT * (1 - outT)))
                {
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(90, 0, 0, 0)), null, rect.Translate(new Vector(0, 4)), h / 2, h / 2);
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(235, 0x1c, 0x20, 0x28)), new Pen(Palette.Brush(_color), 2.5), rect, h / 2, h / 2);
                    ctx.DrawEllipse(Palette.Brush(_color), new Pen(Palette.Brush(Colors.Black), 1.5), new Point(rect.X + 24, y), 9, 9);
                    ctx.DrawText(ft, new Point(rect.X + 44, y - ft.Height / 2));
                }
            }
        }

        /// <summary>A notice that drops onto the board listing the house rules that just changed.</summary>
        sealed class RulesCard : Fx
        {
            readonly IReadOnlyList<string> _lines;

            public RulesCard(AnimationLayer layer, double start, double duration, IReadOnlyList<string> changes) : base(layer, start, duration)
            {
                _lines = changes.Count <= 6 ? changes : changes.Take(5).Append($"and {changes.Count - 5} more").ToList();
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Rect area = L.Area;
                double drop = Ease.BackOut(Ease.Span(t, 0, 0.15));
                double a = Ease.Span(t, 0, 0.08) * (1 - Ease.Span(t, 0.85, 1));
                double w = 40;
                foreach (string line in _lines) w = Math.Max(w, Measure(line, 15).Width);
                w = Math.Min(area.Width - 40, w + 48);
                double h = 58 + _lines.Count * 24;
                var c = new Point(area.Center.X, area.Y + 60 + h / 2 - 40 * (1 - drop));
                var rect = new Rect(c.X - w / 2, c.Y - h / 2, w, h);

                using (ctx.PushOpacity(a))
                {
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(90, 0, 0, 0)), null, rect.Translate(new Vector(0, 5)), 14, 14);
                    ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xf6, 0xec, 0xd2)), new Pen(Palette.Brush(Color.FromRgb(0x9a, 0x7b, 0x4f)), 3), rect, 14, 14);
                    Text(ctx, "House rules changed", new Point(c.X, rect.Y + 24), 18, Palette.Ink);
                    for (int i = 0; i < _lines.Count; i++)
                    {
                        double la = Ease.Span(t, 0.1 + i * 0.04, 0.18 + i * 0.04);
                        using (ctx.PushOpacity(la))
                            Text(ctx, _lines[i], new Point(c.X, rect.Y + 56 + i * 24), 15, Color.FromRgb(0x4a, 0x3a, 0x2a));
                    }
                }
            }
        }

        /// <summary>The winner's name, big, with confetti in their color.</summary>
        sealed class Victory : Fx
        {
            readonly int _player;
            readonly (double X, double Vx, double Vy, double Spin, double Size, int Color, double Delay)[] _bits;

            public Victory(AnimationLayer layer, double start, double duration, int player) : base(layer, start, duration)
            {
                _player = player;
                var rng = new Random(player * 7919 + 17);
                _bits = new (double, double, double, double, double, int, double)[140];
                for (int i = 0; i < _bits.Length; i++)
                    _bits[i] = (rng.NextDouble(), (rng.NextDouble() - 0.5) * 0.25, 0.25 + rng.NextDouble() * 0.35, (rng.NextDouble() - 0.5) * 12,
                        5 + rng.NextDouble() * 6, rng.Next(4), rng.NextDouble() * 0.35);
            }

            public override void Draw(DrawingContext ctx, double t)
            {
                Rect area = L.Area;
                double secs = t * Duration;
                Color mine = Palette.Player(_player);
                Color[] colors = { mine, Palette.Highlight, Colors.White, CardArt.Lighten(mine, 0.5) };

                foreach (var b in _bits)
                {
                    double s = secs - b.Delay;
                    if (s < 0) continue;
                    double x = area.X + area.Width * (b.X + b.Vx * s);
                    double y = area.Y - 20 + area.Height * (b.Vy * s + 0.04 * s * s);
                    if (y > area.Bottom + 20) continue;
                    double wobble = Math.Cos(s * b.Spin);
                    using (ctx.PushTransform(Matrix.CreateScale(wobble, 1) * Matrix.CreateRotation(s * b.Spin * 0.3) * Matrix.CreateTranslation(x, y)))
                        ctx.DrawRectangle(Palette.Brush(colors[b.Color]), null, new Rect(-b.Size / 2, -b.Size * 0.3, b.Size, b.Size * 0.6));
                }

                double a = Ease.Span(t, 0, 0.06) * (1 - Ease.Span(t, 0.88, 1));
                double pop = Ease.BackOut(Ease.Span(t, 0, 0.1));
                var c = new Point(area.Center.X, area.Center.Y - 30);
                using (ctx.PushOpacity(a))
                using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(pop, pop) * Matrix.CreateTranslation(c.X, c.Y)))
                {
                    var rect = new Rect(c.X - 230, c.Y - 62, 460, 124);
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(235, 0x14, 0x17, 0x1c)), new Pen(Palette.Brush(mine), 4), rect, 22, 22);
                    Text(ctx, $"{L.NameOf(_player)} wins!", new Point(c.X, c.Y - 12), 40, Colors.White);
                    Text(ctx, "Victory", new Point(c.X, c.Y + 32), 16, Palette.Highlight);
                }
            }
        }
    }
}
