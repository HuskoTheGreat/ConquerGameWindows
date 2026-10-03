using System;
using System.Collections.Generic;
using Catan.Core;

namespace Catan.Client.Animation
{
    /// <summary>
    /// Animation state the board reads while it draws: pieces popping in, the robber sliding, tiles glowing
    /// after a roll. Purely cosmetic: the board always draws the real game state, these only adjust how.
    /// </summary>
    public sealed class BoardEffects
    {
        public const double PopTime = 0.5, RobberTime = 0.7, GlowTime = 1.7;

        readonly Dictionary<object, double> _pops = new Dictionary<object, double>();
        (Hex From, Hex To, double Start)? _robber;
        (int Number, double Start)? _glow;

        public Func<double> Clock { get; set; } = () => 0;
        public double Speed { get; set; } = 1;

        public double Now => Clock();

        public void Clear()
        {
            _pops.Clear();
            _robber = null;
            _glow = null;
        }

        public void Pop(object piece, double start) => _pops[piece] = start;
        public void MoveRobber(Hex from, Hex to, double start) => _robber = (from, to, start);
        public void Glow(int number, double start) => _glow = (number, start);

        /// <summary>True while any board animation still has frames to draw.</summary>
        public bool Busy
        {
            get
            {
                double now = Now;
                foreach (double s in _pops.Values) if (now < s + PopTime / Speed) return true;
                if (_robber.HasValue && now < _robber.Value.Start + RobberTime / Speed) return true;
                return _glow.HasValue && now < _glow.Value.Start + GlowTime / Speed;
            }
        }

        /// <summary>Scale to draw a piece at: 0 before its pop starts, a springy overshoot, then 1.</summary>
        public double PieceScale(object piece)
        {
            if (!_pops.TryGetValue(piece, out double start)) return 1;
            double t = (Now - start) * Speed / PopTime;
            if (t >= 1)
            {
                _pops.Remove(piece);
                return 1;
            }
            return t <= 0 ? 0 : Ease.BackOut(t);
        }

        /// <summary>Where the robber is between its old and new hex (0..1 along the way), plus how high it hops.</summary>
        public (Hex From, Hex To, double T, double Lift)? Robber()
        {
            if (!_robber.HasValue) return null;
            var r = _robber.Value;
            double t = (Now - r.Start) * Speed / RobberTime;
            if (t >= 1)
            {
                _robber = null;
                return null;
            }
            t = Math.Max(0, t);
            return (r.From, r.To, Ease.InOutCubic(t), Math.Sin(t * Math.PI));
        }

        /// <summary>How brightly tiles with <paramref name="number"/> should glow (0..1).</summary>
        public double GlowFor(int number)
        {
            if (!_glow.HasValue || _glow.Value.Number != number) return 0;
            double t = (Now - _glow.Value.Start) * Speed / GlowTime;
            if (t <= 0 || t >= 1) return 0;
            // Two soft pulses that fade out.
            return Math.Sqrt(1 - t) * (0.7 + 0.3 * Math.Cos(t * Math.PI * 4));
        }
    }

    public static class Ease
    {
        public static double Clamp01(double t) => Math.Clamp(t, 0, 1);
        public static double OutCubic(double t) => 1 - Math.Pow(1 - Clamp01(t), 3);
        public static double InOutCubic(double t)
        {
            t = Clamp01(t);
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }
        public static double BackOut(double t)
        {
            t = Clamp01(t);
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }
        /// <summary>Fraction of the way through [start, end] that <paramref name="t"/> is, clamped.</summary>
        public static double Span(double t, double start, double end) => Clamp01((t - start) / (end - start));
    }
}
