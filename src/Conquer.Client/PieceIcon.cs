using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Conquer.Client
{
    /// <summary>Small glyphs of the board pieces, drawn the same way the board draws them, for buttons and labels.</summary>
    public enum PieceIcon { Road, Village, City }

    public static class PieceIcons
    {
        /// <summary>The piece as a ~18px shape in <paramref name="color"/> with a dark outline.</summary>
        public static Control Draw(this PieceIcon icon, Color color, double size = 18)
        {
            double u = size / 2;
            Point[] shape;
            switch (icon)
            {
                case PieceIcon.Road:
                    // A short plank, tilted like a road on the board.
                    shape = new[] { new Point(0.1 * u, 1.55 * u), new Point(1.55 * u, 0.1 * u), new Point(1.9 * u, 0.45 * u), new Point(0.45 * u, 1.9 * u) };
                    break;
                case PieceIcon.Village:
                    shape = new[]
                    {
                        new Point(0.25 * u, 1.85 * u), new Point(0.25 * u, 0.85 * u), new Point(u, 0.15 * u),
                        new Point(1.75 * u, 0.85 * u), new Point(1.75 * u, 1.85 * u),
                    };
                    break;
                default:
                    shape = new[]
                    {
                        new Point(0.05 * u, 1.85 * u), new Point(0.05 * u, 0.95 * u), new Point(0.85 * u, 0.95 * u),
                        new Point(0.85 * u, 0.5 * u), new Point(1.4 * u, 0.05 * u), new Point(1.95 * u, 0.5 * u),
                        new Point(1.95 * u, 1.85 * u),
                    };
                    break;
            }

            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                g.BeginFigure(shape[0], true);
                for (int i = 1; i < shape.Length; i++) g.LineTo(shape[i]);
                g.EndFigure(true);
            }
            return new Path
            {
                Data = geo,
                Width = size,
                Height = size,
                Fill = Palette.Brush(color),
                Stroke = Palette.Brush(Color.FromRgb(0x10, 0x10, 0x14)),
                StrokeThickness = 1.4,
                StrokeJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}
