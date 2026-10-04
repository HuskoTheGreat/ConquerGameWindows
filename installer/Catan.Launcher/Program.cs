using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Catan.Launcher
{
    /// <summary>
    /// What the Start menu and desktop shortcuts run. Fetches the newest game build from GitHub when there is one,
    /// then starts the game. Offline, it starts whatever build was downloaded last.
    /// </summary>
    static class Program
    {
        const string Title = "Catan";

        [STAThread]
        static int Main(string[] args)
        {
            using var single = new Mutex(true, @"Local\CatanLauncher", out bool first);
            if (!first) return 0;

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Catan");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CatanLauncher/1.0");
            var updater = new Updater(http, UpdateUrl(), root);

            string error = Update(updater);
            if (!updater.HasGame)
            {
                Win32.ShowError(Title, "Catan could not be downloaded. Check your internet connection and try again."
                    + (error == null ? "" : "\n\n" + error));
                return 1;
            }

            Process.Start(new ProcessStartInfo(updater.GameExePath)
            {
                UseShellExecute = false,
                WorkingDirectory = updater.GameDir,
                Arguments = string.Join(" ", args.Select(Quote)),
            });
            return 0;
        }

        /// <summary>Brings the game up to date. Returns why that failed, or null.</summary>
        static string Update(Updater updater)
        {
            bool installed = updater.HasGame;
            UpdateManifest latest;
            try
            {
                // A quick check when a playable copy exists, so a slow network never holds the game up for long.
                using var cts = new CancellationTokenSource(installed ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30));
                latest = Task.Run(() => updater.CheckAsync(cts.Token)).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                return e.Message;
            }
            if (latest == null) return null;

            double fraction = 0;
            var progress = new Progress(p => Volatile.Write(ref fraction, p));
            Task work = Task.Run(() => updater.InstallAsync(latest, progress, CancellationToken.None));

            string verb = installed ? "Updating Catan" : "Downloading Catan";
            Win32.RunSplash(work, () => $"{verb}…  {Volatile.Read(ref fraction):P0}");

            try { work.GetAwaiter().GetResult(); return null; }
            catch (Exception e) { return e.Message; }
        }

        static Uri UpdateUrl()
        {
            string url = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "UpdateUrl").Value;
            return new Uri(url.EndsWith('/') ? url : url + "/");
        }

        static string Quote(string arg) =>
            arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

        // Progress<T> posts to a SynchronizationContext; this reports inline from the download thread instead.
        sealed class Progress : IProgress<double>
        {
            readonly Action<double> report;
            public Progress(Action<double> report) => this.report = report;
            public void Report(double value) => report(value);
        }
    }
}
