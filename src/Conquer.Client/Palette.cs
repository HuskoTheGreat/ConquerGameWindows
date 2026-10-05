using Avalonia.Media;
using Conquer.Core;

namespace Conquer.Client
{
    public static class Palette
    {
        // The sea around the island: bright and friendly.
        public static readonly Color Sea = Color.FromRgb(0x4c, 0xbd, 0xec);
        public static readonly Color SeaDeep = Color.FromRgb(0x23, 0x86, 0xc8);
        public static readonly Color Token = Color.FromRgb(0xff, 0xf6, 0xdc);
        public static readonly Color Ink = Color.FromRgb(0x3a, 0x26, 0x14);
        public static readonly Color Hot = Color.FromRgb(0xd6, 0x28, 0x28);
        public static readonly Color Highlight = Color.FromRgb(0xff, 0xe0, 0x3a);

        // Chrome: a sky-blue table, parchment panels with thick wood-brown outlines, a wooden tray, candy buttons.
        public static readonly Color Sky = Color.FromRgb(0x9a, 0xdc, 0xfb);
        public static readonly Color SkyDeep = Color.FromRgb(0x4f, 0xae, 0xe8);
        public static readonly Color Parchment = Color.FromRgb(0xff, 0xf7, 0xe4);
        public static readonly Color ParchmentDeep = Color.FromRgb(0xf7, 0xe4, 0xb8);
        public static readonly Color Outline = Color.FromRgb(0x6b, 0x44, 0x23);
        public static readonly Color OutlineSoft = Color.FromRgb(0xc9, 0xa4, 0x72);
        public static readonly Color WoodLight = Color.FromRgb(0xd8, 0x9a, 0x58);
        public static readonly Color Wood = Color.FromRgb(0xb9, 0x77, 0x3c);
        public static readonly Color WoodDeep = Color.FromRgb(0x7d, 0x4c, 0x22);

        public static readonly Color Panel = Parchment;
        public static readonly Color PanelEdge = Outline;
        public static readonly Color Window = SkyDeep;
        public static readonly Color SideRaised = Color.FromRgb(0xf3, 0xe3, 0xc0);
        public static readonly Color Control = Color.FromRgb(0xff, 0xfb, 0xf1);
        public static readonly Color ControlHover = Colors.White;
        public static readonly Color ControlPressed = Color.FromRgb(0xf1, 0xe2, 0xc2);
        public static readonly Color ControlEdge = Color.FromRgb(0xa8, 0x7d, 0x4c);
        public static readonly Color Gold = Color.FromRgb(0xff, 0xc6, 0x2e);
        public static readonly Color GoldLight = Color.FromRgb(0xff, 0xd9, 0x5e);
        public static readonly Color GoldDeep = Color.FromRgb(0xd0, 0x8a, 0x00);
        public static readonly Color Orange = Color.FromRgb(0xff, 0x8a, 0x2a);
        public static readonly Color Select = Color.FromRgb(0x3d, 0x9b, 0xf5);
        public static readonly Color SelectLight = Color.FromRgb(0x62, 0xb3, 0xff);
        public static readonly Color SelectDeep = Color.FromRgb(0x1f, 0x62, 0xb5);
        public static readonly Color Text = Color.FromRgb(0x3b, 0x2a, 0x1a);
        public static readonly Color Error = Color.FromRgb(0xd6, 0x28, 0x28);

        public static Color Resource(Resource r)
        {
            switch (r)
            {
                case Core.Resource.Wood: return Color.FromRgb(0x3a, 0x9a, 0x45);
                case Core.Resource.Brick: return Color.FromRgb(0xd9, 0x66, 0x33);
                case Core.Resource.Sheep: return Color.FromRgb(0x96, 0xd6, 0x5a);
                case Core.Resource.Wheat: return Color.FromRgb(0xf5, 0xc4, 0x3a);
                case Core.Resource.Stone: return Color.FromRgb(0x8e, 0x98, 0xa9);
                default: return Color.FromRgb(0xec, 0xd0, 0x8a); // wasteland
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

        /// <summary>Mixes a colour toward white, by <paramref name="amount"/> (0 = unchanged, 1 = white).</summary>
        public static Color Lighten(Color c, double amount) =>
            Color.FromRgb((byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

        /// <summary>A resource's colour deepened enough to read as text on the parchment panels.</summary>
        public static Color ResourceText(Resource r) => Darken(Resource(r), r == Core.Resource.Wheat || r == Core.Resource.Sheep ? 0.62 : 0.78);

        /// <summary>A player's colour, deepened where needed so it reads as text on parchment (white becomes slate).</summary>
        public static Color PlayerText(int id)
        {
            Color c = Player(id);
            if (c.R > 0xe0 && c.G > 0xe0 && c.B > 0xe0) return Color.FromRgb(0x5c, 0x66, 0x78);
            return Luma(c) > 0.55 ? Darken(c, 0.8) : c;
        }

        /// <summary>Ink that reads on top of a player's colour: dark on light colours, white on dark ones.</summary>
        public static Color OnPlayer(int id) => Luma(Player(id)) > 0.6 ? Ink : Colors.White;

        public static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        public static IBrush Brush(Color c) => new SolidColorBrush(c);
    }
}
