using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client
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

        // Chrome: the window, raised surfaces, buttons and the gold accent used for calls to action.
        public static readonly Color Window = Color.FromRgb(0x12, 0x15, 0x1b);
        public static readonly Color Side = Color.FromRgb(0x17, 0x1b, 0x22);
        public static readonly Color SideRaised = Color.FromRgb(0x21, 0x26, 0x2f);
        public static readonly Color SideEdge = Color.FromRgb(0x2a, 0x30, 0x3a);
        public static readonly Color PanelTop = Color.FromRgb(0x2b, 0x31, 0x3c);
        public static readonly Color Control = Color.FromRgb(0x33, 0x39, 0x46);
        public static readonly Color ControlHover = Color.FromRgb(0x40, 0x48, 0x57);
        public static readonly Color ControlPressed = Color.FromRgb(0x2a, 0x2f, 0x3a);
        public static readonly Color ControlEdge = Color.FromRgb(0x4a, 0x52, 0x62);
        public static readonly Color Gold = Color.FromRgb(0xf0, 0xc2, 0x4b);
        public static readonly Color GoldLight = Color.FromRgb(0xff, 0xd8, 0x70);
        public static readonly Color GoldDeep = Color.FromRgb(0xb8, 0x86, 0x22);
        public static readonly Color Select = Color.FromRgb(0x2f, 0x6f, 0xd8);
        public static readonly Color SelectLight = Color.FromRgb(0x4a, 0x8a, 0xf0);
        public static readonly Color Text = Color.FromRgb(0xe8, 0xec, 0xf3);

        public static Color Resource(Resource r)
        {
            switch (r)
            {
                case Core.Resource.Timber: return Color.FromRgb(0x2c, 0x6b, 0x35);
                case Core.Resource.Clay: return Color.FromRgb(0xb8, 0x52, 0x33);
                case Core.Resource.Livestock: return Color.FromRgb(0x8f, 0xcb, 0x5c);
                case Core.Resource.Grain: return Color.FromRgb(0xed, 0xc7, 0x40);
                case Core.Resource.Iron: return Color.FromRgb(0x7d, 0x84, 0x94);
                default: return Color.FromRgb(0xdc, 0xc9, 0x8f); // wasteland
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
