using Avalonia.Media;
using Catan.Core;

namespace Catan.Client
{
    public static class Palette
    {
        public static readonly Color Sea = Color.FromRgb(0x1d, 0x4e, 0x7a);
        public static readonly Color SeaDeep = Color.FromRgb(0x12, 0x33, 0x52);
        public static readonly Color Token = Color.FromRgb(0xf5, 0xec, 0xcf);
        public static readonly Color Ink = Color.FromRgb(0x1f, 0x1a, 0x14);
        public static readonly Color Hot = Color.FromRgb(0xc0, 0x1f, 0x1f);
        public static readonly Color Highlight = Color.FromRgb(0xff, 0xe0, 0x3a);
        public static readonly Color Panel = Color.FromRgb(0x24, 0x28, 0x30);
        public static readonly Color PanelEdge = Color.FromRgb(0x3a, 0x40, 0x4c);

        public static Color Resource(Resource r)
        {
            switch (r)
            {
                case Core.Resource.Wood: return Color.FromRgb(0x2c, 0x6b, 0x35);
                case Core.Resource.Brick: return Color.FromRgb(0xb8, 0x52, 0x33);
                case Core.Resource.Sheep: return Color.FromRgb(0x8f, 0xcb, 0x5c);
                case Core.Resource.Wheat: return Color.FromRgb(0xed, 0xc7, 0x40);
                case Core.Resource.Ore: return Color.FromRgb(0x7d, 0x84, 0x94);
                default: return Color.FromRgb(0xdc, 0xc9, 0x8f); // desert
            }
        }

        static readonly Color[] Players =
        {
            Color.FromRgb(0xdc, 0x32, 0x32), // red
            Color.FromRgb(0x33, 0x73, 0xe6), // blue
            Color.FromRgb(0xf2, 0xf2, 0xf2), // white
            Color.FromRgb(0xf2, 0x8c, 0x1a), // orange
            Color.FromRgb(0x33, 0xb3, 0x59), // green
            Color.FromRgb(0xa6, 0x4d, 0xcc), // purple
        };

        public static Color Player(int id) => Players[id % Players.Length];

        public static Color Darken(Color c, double factor) =>
            Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

        public static IBrush Brush(Color c) => new SolidColorBrush(c);
    }
}
