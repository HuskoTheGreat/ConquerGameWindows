using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Conquer.Client
{
    /// <summary>
    /// The game's look on top of the Fluent theme (light variant): chunky rounded "candy" buttons with a darker
    /// bottom edge that press down when clicked, in four kinds: plain cream, <c>primary</c> gold for the main call
    /// to action, <c>selected</c> blue for a picked choice, <c>menu</c> for the big title-screen entries and
    /// <c>quiet</c> for low-key extras. Built in code like the rest of the interface.
    /// </summary>
    public static class GameTheme
    {
        public const string Primary = "primary";
        public const string Selected = "selected";
        public const string Menu = "menu";
        public const string Quiet = "quiet";
        public const string Round = "round";

        public static FluentTheme Fluent() => new FluentTheme
        {
            Palettes =
            {
                [ThemeVariant.Light] = new ColorPaletteResources
                {
                    Accent = Palette.Select,
                    RegionColor = Palette.Parchment,
                    BaseHigh = Palette.Text,
                },
            },
        };

        static readonly ITransform Rest = TransformOperations.Parse("translateY(0px)");
        static readonly ITransform Down = TransformOperations.Parse("translateY(2px)");

        public static Styles Build()
        {
            var styles = new Styles();

            styles.Add(Style(x => x.OfType<Button>(),
                (Button.CornerRadiusProperty, new CornerRadius(20)),
                (Button.FontSizeProperty, 15.0),
                (Button.FontWeightProperty, FontWeight.Bold),
                (Button.FontFamilyProperty, Ui.Font),
                (Button.BorderThicknessProperty, new Thickness(2, 2, 2, 5))));
            styles.Add(Style(x => x.OfType<Button>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.RenderTransformProperty, Rest),
                (ContentPresenter.TransitionsProperty, new Transitions
                {
                    new TransformOperationsTransition { Property = ContentPresenter.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(70) },
                })));
            styles.Add(Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.RenderTransformProperty, Down)));

            // Plain buttons: cream candy with a toffee edge.
            Look(styles, null, Palette.Control, Palette.ControlHover, Palette.ControlPressed, Palette.ControlEdge, Palette.Text);
            // The main call to action: sunny gold with dark ink.
            Look(styles, Primary, Palette.Gold, Palette.GoldLight, Palette.Darken(Palette.Gold, 0.92), Palette.GoldDeep, Palette.Ink);
            // A picked option in a row of choices.
            Look(styles, Selected, Palette.Select, Palette.SelectLight, Palette.Darken(Palette.Select, 0.9), Palette.SelectDeep, Colors.White);
            // Big title-screen menu entries.
            Look(styles, Menu, Palette.Orange, Palette.Lighten(Palette.Orange, 0.18), Palette.Darken(Palette.Orange, 0.92), Color.FromRgb(0xb8, 0x50, 0x00), Colors.White);
            // Low-key extras (house rules, new game, quit).
            Look(styles, Quiet, Color.FromArgb(0xb0, 0xff, 0xfb, 0xf1), Palette.Control, Palette.ControlPressed, Palette.OutlineSoft, Palette.Text);

            styles.Add(Style(x => x.OfType<Button>().Class(Quiet),
                (Button.FontSizeProperty, 13.0),
                (Button.BorderThicknessProperty, new Thickness(2, 2, 2, 4))));
            styles.Add(Style(x => x.OfType<Button>().Class(Round),
                (Button.CornerRadiusProperty, new CornerRadius(18)),
                (Button.FontSizeProperty, 17.0)));

            styles.Add(Style(x => x.OfType<ToolTip>(),
                (ToolTip.BackgroundProperty, Palette.Brush(Palette.Parchment)),
                (ToolTip.BorderBrushProperty, Palette.Brush(Palette.Outline)),
                (ToolTip.BorderThicknessProperty, new Thickness(2)),
                (ToolTip.CornerRadiusProperty, new CornerRadius(10)),
                (ToolTip.FontFamilyProperty, Ui.Font),
                (ToolTip.ForegroundProperty, Palette.Brush(Palette.Text))));

            styles.Add(Style(x => x.OfType<CheckBox>(),
                (CheckBox.ForegroundProperty, Palette.Brush(Palette.Text)),
                (CheckBox.FontWeightProperty, FontWeight.SemiBold)));
            styles.Add(Style(x => x.OfType<TextBox>(),
                (TextBox.CornerRadiusProperty, new CornerRadius(12)),
                (TextBox.BorderThicknessProperty, new Thickness(2)),
                (TextBox.BackgroundProperty, Palette.Brush(Colors.White)),
                (TextBox.BorderBrushProperty, Palette.Brush(Palette.OutlineSoft))));
            styles.Add(Style(x => x.OfType<ScrollBar>(),
                (ScrollBar.OpacityProperty, 0.8)));
            return styles;
        }

        /// <summary>Background, edge and text for every state of one kind of button.</summary>
        static void Look(Styles styles, string cls, Color rest, Color hover, Color pressed, Color edge, Color text)
        {
            Selector Presenter(string pseudo)
            {
                Selector s = Selectors.OfType<Button>(null);
                if (cls != null) s = s.Class(cls);
                if (pseudo != null) s = s.Class(pseudo);
                return s.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            }

            // A glossy top half, like a boiled sweet.
            IBrush Fill(Color c) => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Palette.WithAlpha(Palette.Lighten(c, 0.28), c.A), 0),
                    new GradientStop(c, 0.55),
                    new GradientStop(Palette.WithAlpha(Palette.Darken(c, 0.94), c.A), 1),
                },
            };

            // The resting look also goes on the button itself, since the template binds to it.
            if (cls == null)
                styles.Add(Style(x => x.OfType<Button>(),
                    (Button.BackgroundProperty, Fill(rest)),
                    (Button.BorderBrushProperty, Palette.Brush(edge)),
                    (Button.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(null),
                (ContentPresenter.BackgroundProperty, Fill(rest)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(edge)),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":pointerover"),
                (ContentPresenter.BackgroundProperty, Fill(hover)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(Palette.Darken(edge, 0.9))),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":pressed"),
                (ContentPresenter.BackgroundProperty, Fill(pressed)),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(edge)),
                (ContentPresenter.ForegroundProperty, Palette.Brush(text))));
            styles.Add(Style(_ => Presenter(":disabled"),
                (ContentPresenter.BackgroundProperty, Palette.Brush(Color.FromArgb(0x80, 0xee, 0xe4, 0xd0))),
                (ContentPresenter.BorderBrushProperty, Palette.Brush(Color.FromArgb(0x70, 0xa8, 0x90, 0x70))),
                (ContentPresenter.ForegroundProperty, Palette.Brush(Color.FromArgb(0x90, 0x7a, 0x68, 0x55)))));
        }

        static Style Style(Func<Selector, Selector> selector, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var style = new Style(selector);
            foreach (var (property, value) in setters) style.Setters.Add(new Setter(property, value));
            return style;
        }
    }
}
