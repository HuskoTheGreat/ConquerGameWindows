using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Conquer.Launcher
{
    /// <summary>
    /// What the Start menu and desktop shortcuts run. Fetches the newest game build from GitHub when there is one,
    /// then starts the game. Offline, it starts whatever build was downloaded last.
    /// </summary>
    static class Program
    {
        const string Title = "Conquer";

        [STAThread]
        static int Main(string[] args)
        {
            using var single = new Mutex(true, @"Local\ConquerLauncher", out bool first);
            if (!first) return 0;

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Conquer");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ConquerLauncher/1.0");
            var updater = new Updater(http, UpdateUrl(), root, UpdatePublicKey());

            string error = Update(updater);
            if (!updater.HasGame)
            {
                Win32.ShowError(Title, "Conquer could not be downloaded. Check your internet connection and try again."
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

            string verb = installed ? "Updating Conquer" : "Downloading Conquer";
            Win32.RunSplash(work, () => $"{verb}…  {Volatile.Read(ref fraction):P0}");

            try { work.GetAwaiter().GetResult(); return null; }
            catch (Exception e) { return e.Message; }
        }

        static Uri UpdateUrl()
        {
            string url = Metadata("UpdateUrl");
            return new Uri(url.EndsWith('/') ? url : url + "/");
        }

        /// <summary>The release key's public half, baked in at build time from installer/update-public-key.txt.</summary>
        static byte[] UpdatePublicKey()
        {
            string key = Metadata("UpdatePublicKey");
            try { return string.IsNullOrWhiteSpace(key) ? null : Convert.FromBase64String(key.Trim()); }
            catch (FormatException) { return null; }
        }

        static string Metadata(string key) => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

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
