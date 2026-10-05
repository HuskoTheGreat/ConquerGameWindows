using System;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Conquer.Client.Animation;

namespace Conquer.Client
{
    /// <summary>
    /// The title screen: a drifting field of tiles behind the wordmark and the main menu. While it shows (and
    /// while the new-game, connect and lobby forms sit on top of it) the game panels are hidden; they fade back
    /// in when a game starts.
    /// </summary>
    public partial class MainWindow
    {
        static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(420);

        readonly TitleBackdrop _backdrop = new TitleBackdrop { Name = "TitleBackdrop" };
        Grid _main;
        bool? _titleShown;

        /// <summary>True while the title screen (or a form over it) is up instead of a game.</summary>
        public bool ShowingTitle => _titleShown == true;

        bool WantsTitle() =>
            _c.Game == null || _modal == Modal.Start || _modal == Modal.Online || _modal == Modal.Lan || _modal == Modal.Setup;

        /// <summary>Swaps between the title backdrop and the game panels, cross-fading when animations are on.</summary>
        void UpdateTitle()
        {
            bool title = WantsTitle();
            if (_titleShown == title) return;
            bool animate = _titleShown.HasValue && AnimationLayer.Enabled;
            _titleShown = title;

            _main.Transitions = animate
                ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = FadeTime, Easing = new CubicEaseInOut() } }
                : null;
            _main.Opacity = title ? 0 : 1;
            _main.IsHitTestVisible = !title;
            _main.IsEnabled = !title; // keeps keyboard focus out of the hidden panels

            if (title) _backdrop.IsVisible = true;
            else if (animate) DispatcherTimer.RunOnce(() => _backdrop.IsVisible = _titleShown == true, FadeTime);
            else _backdrop.IsVisible = false;
        }

        // ---- Start screen --------------------------------------------------------------------------

        Control BuildStart()
        {
            var menu = Ui.Column(12,
                MenuButton("Single player", "Against computer players, or friends passing the device.", () => OpenModal(Modal.Setup), GameTheme.Primary),
                MenuButton("Online", "With friends over the internet, through a game server.", () =>
                {
                    _netError = null;
                    _netForm = Modal.Online;
                    OpenModal(Modal.Online);
                }, GameTheme.Menu),
                MenuButton("Local network", "Host a game here, or join one on the same Wi-Fi or network.", OpenLan, GameTheme.Selected));
            if (_c.Game != null) menu.Children.Add(MenuButton("Back to game", "Pick up where you left off.", CloseModal, GameTheme.Selected));

            Button quit = Ui.Button("Quit", Close, minWidth: 120);
            quit.Classes.Add(GameTheme.Quiet);
            quit.HorizontalAlignment = HorizontalAlignment.Center;
            quit.Margin = new Thickness(0, 4, 0, 0);
            menu.Children.Add(quit);
            Border card = Ui.Card(menu);
            card.Padding = new Thickness(28, 22, 28, 18);
            card.HorizontalAlignment = HorizontalAlignment.Center;

            var col = Ui.Column(0, Wordmark(), new Border { Height = 26 }, card);
            col.Margin = new Thickness(0, 0, 0, 30);
            return col;
        }

        /// <summary>The small version stamp in the corner of the title screen.</summary>
        static Control VersionStamp() => new TextBlock
        {
            Text = VersionText(),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Brush(Color.FromArgb(0xc0, 0xff, 0xff, 0xff)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 12),
            IsHitTestVisible = false,
        };

        /// <summary>The game's name as a chunky cartoon logo, with the tagline on a ribbon below.</summary>
        static Control Wordmark()
        {
            var logo = new LogoText("CONQUER", 104) { HorizontalAlignment = HorizontalAlignment.Center };

            var tagline = new TextBlock
            {
                Text = "BUILD  ·  TRADE  ·  CONQUER THE ISLAND",
                FontSize = 14,
                FontWeight = FontWeight.Black,
                LetterSpacing = 2,
                Foreground = Palette.Brush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var ribbon = new Border
            {
                Child = tagline,
                Background = Palette.Brush(Palette.SelectDeep),
                BorderBrush = Palette.Brush(Color.FromRgb(0x10, 0x3a, 0x6c)),
                BorderThickness = new Thickness(3, 3, 3, 5),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(20, 6, 18, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                BoxShadow = BoxShadows.Parse("0 6 14 0 #40000000"),
            };

            var col = Ui.Column(0, Emblem(), logo, ribbon);
            col.HorizontalAlignment = HorizontalAlignment.Center;
            return col;
        }

        /// <summary>Three little island tiles: the mark above the name.</summary>
        static Control Emblem() => new EmblemControl { Width = 92, Height = 74, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -6) };

        sealed class EmblemControl : Control
        {
            public override void Render(DrawingContext ctx)
            {
                const double r = 22;
                double w = Math.Sqrt(3) * r;
                var mid = new Point(Bounds.Width / 2, Bounds.Height / 2 + r * 0.55);
                var tiles = new[]
                {
                    (new Point(mid.X, mid.Y - 1.5 * r), Core.Resource.Stone),
                    (new Point(mid.X - w / 2, mid.Y), Core.Resource.Wood),
                    (new Point(mid.X + w / 2, mid.Y), Core.Resource.Wheat),
                };
                var ink = new Pen(Palette.Brush(Palette.Outline), 3.2 / (r * 0.95), lineJoin: PenLineJoin.Round);
                foreach (var (c, res) in tiles)
                {
                    TileArt.DrawHex(ctx, res, 0, c, r * 0.95);
                    using (ctx.PushTransform(Matrix.CreateScale(r * 0.95, r * 0.95) * Matrix.CreateTranslation(c.X, c.Y)))
                        ctx.DrawGeometry(null, ink, TileArt.UnitHex);
                }
            }
        }

        static Control MenuButton(string title, string hint, Action onClick, string look)
        {
            Button b = Ui.Button(title, onClick, minWidth: 340);
            b.Classes.Add(look);
            b.Padding = new Thickness(24, 12, 24, 12);
            b.FontSize = 21;
            b.FontWeight = FontWeight.Black;
            b.CornerRadius = new CornerRadius(26);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            var note = Ui.Text(hint, 12, true, Ui.Muted);
            note.TextAlignment = TextAlignment.Center;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            return Ui.Column(6, b, note);
        }

        /// <summary>The version this build was stamped with, without the source revision the SDK appends.</summary>
        static string VersionText()
        {
            Assembly assembly = typeof(MainWindow).Assembly;
            string v = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "";
            int plus = v.IndexOf('+');
            if (plus >= 0) v = v.Substring(0, plus);
            return v.Length == 0 ? "" : "v" + v;
        }
    }

    /// <summary>
    /// Big cartoon lettering: the text's outline is turned into geometry, extruded downward in deep orange,
    /// inked with a thick brown outline and filled with a sunny gradient and a glossy highlight.
    /// </summary>
    public sealed class LogoText : Control
    {
        readonly Geometry _glyphs;
        readonly Rect _box;

        public LogoText(string text, double size)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Ui.Font, FontStyle.Normal, FontWeight.Black), size, Brushes.White);
            _glyphs = ft.BuildGeometry(new Point(0, 0));
            _box = _glyphs?.Bounds ?? new Rect(0, 0, ft.Width, ft.Height);
            Width = _box.Width + 24;
            Height = _box.Height + 30;
        }

        public override void Render(DrawingContext ctx)
        {
            if (_glyphs == null) return;
            var origin = Matrix.CreateTranslation(12 - _box.X, 8 - _box.Y);
            var ink = Palette.Brush(Color.FromRgb(0x5a, 0x2a, 0x0c));
            using (ctx.PushTransform(origin))
            {
                // Soft shadow on the sea, then the extruded sides, then the inked face.
                using (ctx.PushTransform(Matrix.CreateTranslation(4, 16)))
                    ctx.DrawGeometry(Palette.Brush(Color.FromArgb(0x40, 0x0a, 0x30, 0x60)), new Pen(Palette.Brush(Color.FromArgb(0x40, 0x0a, 0x30, 0x60)), 12, lineJoin: PenLineJoin.Round), _glyphs);
                for (int k = 9; k >= 1; k--)
                {
                    using (ctx.PushTransform(Matrix.CreateTranslation(0, k)))
                        ctx.DrawGeometry(ink, new Pen(ink, 10, lineJoin: PenLineJoin.Round), _glyphs);
                }
                ctx.DrawGeometry(null, new Pen(ink, 10, lineJoin: PenLineJoin.Round), _glyphs);
                ctx.DrawGeometry(null, new Pen(Palette.Brush(Color.FromRgb(0xff, 0xf6, 0xd8)), 4, lineJoin: PenLineJoin.Round), _glyphs);
                ctx.DrawGeometry(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xff, 0xf0, 0x7a), 0),
                        new GradientStop(Color.FromRgb(0xff, 0xc8, 0x2e), 0.5),
                        new GradientStop(Color.FromRgb(0xff, 0x8a, 0x1e), 1),
                    },
                }, null, _glyphs);
                // A glossy band across the top of the letters.
                using (ctx.PushGeometryClip(_glyphs))
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)), null, new Rect(_box.X, _box.Y, _box.Width, _box.Height * 0.36));
            }
        }
    }
}
