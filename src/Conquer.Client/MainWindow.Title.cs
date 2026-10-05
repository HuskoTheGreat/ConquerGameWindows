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
            _c.Game == null || _modal == Modal.Start || _modal == Modal.Online || _modal == Modal.Setup;

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
            var menu = Ui.Column(14,
                MenuButton("Single player", "Against computer players, or friends passing the device.", () => OpenModal(Modal.Setup)),
                MenuButton("Online", "With friends over the internet, through a game server.", () =>
                {
                    _netError = null;
                    OpenModal(Modal.Online);
                }));
            if (_c.Game != null) menu.Children.Add(MenuButton("Back to game", "Pick up where you left off.", CloseModal));

            Button quit = Ui.Button("Quit", Close, minWidth: 120);
            quit.Classes.Add(GameTheme.Quiet);
            quit.HorizontalAlignment = HorizontalAlignment.Center;
            quit.Margin = new Thickness(0, 8, 0, 0);
            menu.Children.Add(quit);
            menu.HorizontalAlignment = HorizontalAlignment.Center;

            var col = Ui.Column(0, Wordmark(), new Border { Height = 44 }, menu);
            col.Margin = new Thickness(0, 0, 0, 30);
            return col;
        }

        /// <summary>The small version stamp in the corner of the title screen.</summary>
        static Control VersionStamp() => new TextBlock
        {
            Text = VersionText(),
            FontSize = 11,
            Foreground = Palette.Brush(Color.FromArgb(0x90, 0xa4, 0xac, 0xbc)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 12),
            IsHitTestVisible = false,
        };

        /// <summary>The game's name set large in gold, with a tile emblem above and the tagline below.</summary>
        static Control Wordmark()
        {
            var gold = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xff, 0xef, 0xbe), 0),
                    new GradientStop(Palette.Gold, 0.55),
                    new GradientStop(Palette.GoldDeep, 1),
                },
            };
            const double spacing = 16;
            var name = new TextBlock
            {
                Text = "CONQUER",
                FontFamily = new FontFamily("Georgia, Cambria, Palatino Linotype, Liberation Serif, DejaVu Serif, serif"),
                FontSize = 100,
                FontWeight = FontWeight.Bold,
                LetterSpacing = spacing,
                Foreground = gold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(spacing, 0, 0, 0), // letter spacing trails the last letter; this re-centres the word
                Effect = new DropShadowEffect { BlurRadius = 26, OffsetX = 0, OffsetY = 6, Color = Colors.Black, Opacity = 0.9 },
            };

            var tagline = new TextBlock
            {
                Text = "BUILD  ·  TRADE  ·  CONQUER THE ISLAND",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 3,
                Foreground = Palette.Brush(Color.FromRgb(0xd9, 0xc8, 0x9a)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0),
            };
            var taglineRow = Ui.Row(0, GoldRule(fadeLeft: true), tagline, GoldRule(fadeLeft: false));
            taglineRow.HorizontalAlignment = HorizontalAlignment.Center;

            var col = Ui.Column(2, Emblem(), name, taglineRow);
            col.HorizontalAlignment = HorizontalAlignment.Center;
            return col;
        }

        static Control GoldRule(bool fadeLeft) => new Border
        {
            Height = 1,
            Width = 96,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(fadeLeft ? Colors.Transparent : Palette.Gold, 0),
                    new GradientStop(fadeLeft ? Palette.Gold : Colors.Transparent, 1),
                },
            },
        };

        /// <summary>Three tiles in gold outline: the mark above the name.</summary>
        static Control Emblem()
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                const double r = 15;
                double w = Math.Sqrt(3) * r;
                foreach (Point c in new[] { new Point(-w / 2, 0), new Point(w / 2, 0), new Point(0, -1.5 * r) })
                {
                    for (int i = 0; i < 6; i++)
                    {
                        double a = Math.PI / 180 * (60 * i - 30);
                        var p = new Point(c.X + r * 0.88 * Math.Cos(a), c.Y + r * 0.88 * Math.Sin(a));
                        if (i == 0) g.BeginFigure(p, true);
                        else g.LineTo(p);
                    }
                    g.EndFigure(true);
                }
            }
            return new Avalonia.Controls.Shapes.Path
            {
                Data = geo,
                Fill = Palette.Brush(Color.FromArgb(0x38, Palette.Gold.R, Palette.Gold.G, Palette.Gold.B)),
                Stroke = Palette.Brush(Palette.Gold),
                StrokeThickness = 2,
                StrokeJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
        }

        static Control MenuButton(string title, string hint, Action onClick)
        {
            Button b = Ui.Button(title, onClick, minWidth: 340);
            b.Classes.Add(GameTheme.Menu);
            b.Padding = new Thickness(24, 13);
            b.FontSize = 19;
            b.FontWeight = FontWeight.SemiBold;
            b.CornerRadius = new CornerRadius(10);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            var note = Ui.Text(hint, 12, false, Color.FromRgb(0x8d, 0x96, 0xa8));
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
}
