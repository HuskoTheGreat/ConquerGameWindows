using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Conquer.Client;
using Conquer.Client.Animation;

namespace Conquer.Local
{
    /// <summary>The main client's window, opened as a purely local game.</summary>
    public sealed class LocalApp : Application
    {
        public static LocalOptions Options { get; set; } = new LocalOptions();

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = OpenWindow(Options);
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Builds the window and, for a quick start, starts the game without the new-game screen.</summary>
        public static MainWindow OpenWindow(LocalOptions o)
        {
            AnimationLayer.Enabled = o.Animations;
            var window = new MainWindow { Title = "Conquer (local)", OfflineOnly = true };
            if (o.QuickStart) window.StartNewGame(o.Players, o.Radius, o.VictoryPoints, o.HideHands, o.Seed);
            return window;
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            LocalOptions options;
            AttachToParentConsole();
            try
            {
                options = LocalOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }

            if (options.ShowHelp)
            {
                Console.WriteLine(LocalOptions.Usage);
                return 0;
            }

            LocalApp.Options = options;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // A Windows GUI app has no console of its own, so --help and option errors would vanish. Borrow the
        // console of the command prompt that started us, if there is one.
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        static void AttachToParentConsole()
        {
            if (!OperatingSystem.IsWindows()) return;
            try { AttachConsole(-1); } catch { }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<LocalApp>().UsePlatformDetect().LogToTrace();
    }
}
