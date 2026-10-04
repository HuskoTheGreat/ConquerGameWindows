using System;

namespace Conquer.Core
{
    /// <summary>
    /// Axial coordinates to 2D plane positions (pointy-top). Engine-agnostic: a view maps (X, Y)
    /// onto its own screen or world space. Size is the hex center-to-corner distance.
    /// </summary>
    public static class HexLayout
    {
        static readonly float Sqrt3 = (float)Math.Sqrt(3.0);

        public static (float X, float Y) ToPlane(Hex h, float size = 1f) =>
            (size * (Sqrt3 * h.Q + Sqrt3 / 2f * h.R), size * (1.5f * h.R));

        public static (float X, float Y) ToPlane(Vertex v, float size = 1f) => Average(size, v.A, v.B, v.C);

        public static (float X, float Y) ToPlane(Edge e, float size = 1f) => Average(size, e.A, e.B);

        /// <summary>The hex containing a plane point (for mouse picking).</summary>
        public static Hex FromPlane(float x, float y, float size = 1f)
        {
            float q = (Sqrt3 / 3f * x - 1f / 3f * y) / size;
            float r = (2f / 3f * y) / size;
            return Round(q, r);
        }

        static Hex Round(float qf, float rf)
        {
            float sf = -qf - rf;
            int q = (int)Math.Round(qf);
            int r = (int)Math.Round(rf);
            int s = (int)Math.Round(sf);
            float dq = Math.Abs(q - qf);
            float dr = Math.Abs(r - rf);
            float ds = Math.Abs(s - sf);
            if (dq > dr && dq > ds) q = -r - s;
            else if (dr > ds) r = -q - s;
            return new Hex(q, r);
        }

        static (float X, float Y) Average(float size, params Hex[] hexes)
        {
            float x = 0f, y = 0f;
            foreach (Hex h in hexes)
            {
                var p = ToPlane(h, size);
                x += p.X;
                y += p.Y;
            }
            return (x / hexes.Length, y / hexes.Length);
        }
    }
}
