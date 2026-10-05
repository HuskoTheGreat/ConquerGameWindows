using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Conquer.Client
{
    /// <summary>
    /// The game's look on top of the Fluent theme: a gold accent, rounded buttons with clear hover and pressed
    /// states, and three button kinds (plain, <c>primary</c> for the main call to action, <c>selected</c> for a
    /// picked choice). Built in code like the rest of the interface.
    /// </summary>
    public static class GameTheme
    {
        public const string Primary = "primary";
        public const string Selected = "selected";
        public const string Menu = "menu";
        public const string Quiet = "quiet";

        public static FluentTheme Fluent() => new FluentTheme
        {
            Palettes =
            {
                [ThemeVariant.Dark] = new ColorPaletteResources
                {
                    Accent = Palette.Gold,
                    RegionColor = Palette.Window,
                },
            },
        };

        public static Styles Build()
        {
            var styles = new Styles();

            styles.Add(Style(x => x.OfType<Button>(),
                (Button.CornerRadiusProperty, new CornerRadius(8)),
                (Button.FontSizeProperty, 14.0),
                (Button.BorderThicknessProperty, new Thickness(1))));

            // Plain buttons.
            Look(styles, null, Palette.Control, Palette.ControlHover, Palette.ControlPressed, Palette.ControlEdge, Palette.Text);
            // The main call to action: warm gold with dark ink.
            Look(styles, Primary, Palette.Gold, Palette.GoldLight, Palette.GoldDeep, Palette.GoldDeep, Palette.Ink, gradient: true);
            // A picked option in a row of choices.
            Look(styles, Selected, Palette.Select, Palette.SelectLight, Palette.Darken(Palette.Select, 0.8), Color.FromRgb(0x9c, 0xc2, 0xff), Colors.White);
            // Big title-screen menu entries.
            Look(styles, Menu, Color.FromArgb(0xd8, 0x1c, 0x21, 0x2a), Color.FromArgb(0xf0, 0x2a, 0x31, 0x3d), Color.FromArgb(0xf0, 0x16, 0x1a, 0x21),
                Color.FromArgb(0x90, Palette.Gold.R, Palette.Gold.G, Palette.Gold.B), Palette.Text);
            styles.Add(Style(x => x.OfType<Button>().Class(Menu).Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(Palette.GoldLight)),
                (ContentPresenter.ForegroundProperty, Palette.Brush(Palette.GoldLight))));
            // Low-key buttons in the top-right corner of the action bar.
            Look(styles, Quiet, Colors.Transparent, Palette.Control, Palette.ControlPressed, Palette.PanelEdge, Ui.Muted);

            styles.Add(Style(x => x.OfType<Button>().Class(Primary).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.FontWeightProperty, FontWeight.Bold)));

            styles.Add(Style(x => x.OfType<ToolTip>(),
                (ToolTip.BackgroundProperty, Palette.Brush(Color.FromRgb(0x1b, 0x1f, 0x27))),
                (ToolTip.BorderBrushProperty, Palette.Brush(Palette.ControlEdge)),
                (ToolTip.ForegroundProperty, Palette.Brush(Palette.Text))));

            styles.Add(Style(x => x.OfType<CheckBox>(),
                (CheckBox.ForegroundProperty, Palette.Brush(Palette.Text))));
            return styles;
        }

        /// <summary>Background, border and text for every state of one kind of button.</summary>
        static void Look(Styles styles, string cls, Color rest, Color hover, Color pressed, Color edge, Color text, bool gradient = false)
        {
            Selector Presenter(string pseudo)
            {
                Selector s = Selectors.OfType<Button>(null);
                if (cls != null) s = s.Class(cls);
                if (pseudo != null) s = s.Class(pseudo);
                return s.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            }

            IBrush Fill(Color c) => gradient
                ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(CardLighten(c, 0.18), 0), new GradientStop(c, 1) },
                }
                : Palette.Brush(c);

            styles.Add(Style(_ => Presenter(null),
                (ContentPresenter.BackgroundProperty, Fill(rest)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(edge)),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":pointerover"),
                (ContentPresenter.BackgroundProperty, Fill(hover)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(CardLighten(edge, 0.25))),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":pressed"),
                (ContentPresenter.BackgroundProperty, Fill(pressed)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(edge)),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":disabled"),
                (ContentPresenter.BackgroundProperty, Palette.Brush(Color.FromArgb(0x70, rest.R, rest.G, rest.B))),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(Color.FromArgb(0x50, edge.R, edge.G, edge.B))),
                (ContentPresenter.ForegroundProperty, Palette.Brush(Color.FromArgb(0x80, text.R, text.G, text.B)))));
        }

        static Color CardLighten(Color c, double amount) => Color.FromArgb(c.A,
            (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

        static Style Style(Func<Selector, Selector> selector, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var style = new Style(selector);
            foreach (var (property, value) in setters) style.Setters.Add(new Setter(property, value));
            return style;
        }
    }
}
