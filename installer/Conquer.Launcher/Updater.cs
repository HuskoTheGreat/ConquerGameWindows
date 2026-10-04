using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Conquer.Launcher
{
    /// <summary>
    /// Keeps a copy of the game in <c>root\game</c> in step with the build CI publishes at <c>baseUrl</c>.
    /// An update is downloaded, checked against the manifest's SHA-256, unpacked next to the current copy and only
    /// then swapped in, so a failed or interrupted update always leaves the previous game playable.
    /// </summary>
    public sealed class Updater
    {
        public const string ManifestName = "latest.json";
        public const string GameExe = "Conquer.exe";
        const string VersionFile = "version.json";

        readonly HttpClient http;
        readonly Uri baseUrl;
        readonly string root;

        public Updater(HttpClient http, Uri baseUrl, string root)
        {
            this.http = http;
            this.baseUrl = baseUrl;
            this.root = root;
        }

        public string GameDir => Path.Combine(root, "game");
        public string GameExePath => Path.Combine(GameDir, GameExe);
        public bool HasGame => File.Exists(GameExePath);

        /// <summary>The installed build's version, or null when nothing is installed.</summary>
        public string InstalledVersion
        {
            get
            {
                string path = Path.Combine(GameDir, VersionFile);
                if (!HasGame || !File.Exists(path)) return null;
                try { return UpdateManifest.Parse(File.ReadAllText(path))?.Version; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Returns the published manifest when it differs from the installed build, otherwise null.</summary>
        public async Task<UpdateManifest> CheckAsync(CancellationToken ct)
        {
            string json = await http.GetStringAsync(new Uri(baseUrl, ManifestName), ct);
            UpdateManifest latest = UpdateManifest.Parse(json);
            if (latest == null || string.IsNullOrEmpty(latest.Version) || string.IsNullOrEmpty(latest.File)
                || string.IsNullOrEmpty(latest.Sha256))
                throw new InvalidDataException("The update manifest is incomplete.");
            if (latest.File.Contains('/') || latest.File.Contains('\\') || latest.File.Contains(".."))
                throw new InvalidDataException("The update manifest names an invalid file.");
            return latest.Version == InstalledVersion ? null : latest;
        }

        /// <summary>Downloads, verifies and installs <paramref name="update"/>. Progress runs from 0 to 1.</summary>
        public async Task InstallAsync(UpdateManifest update, IProgress<double> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "download.zip");
            string staging = Path.Combine(root, "game.new");
            string old = Path.Combine(root, "game.old");

            try
            {
                await DownloadAsync(update, zip, progress, ct);

                DeleteDir(staging);
                ZipFile.ExtractToDirectory(zip, staging);
                if (!File.Exists(Path.Combine(staging, GameExe)))
                    throw new InvalidDataException($"The downloaded build has no {GameExe}.");
                File.WriteAllText(Path.Combine(staging, VersionFile), update.ToJson());

                DeleteDir(old);
                if (Directory.Exists(GameDir)) Directory.Move(GameDir, old);
                try { Directory.Move(staging, GameDir); }
                catch
                {
                    if (Directory.Exists(old) && !Directory.Exists(GameDir)) Directory.Move(old, GameDir);
                    throw;
                }
                TryDeleteDir(old);
            }
            finally
            {
                TryDelete(zip);
                TryDeleteDir(staging);
            }
        }

        async Task DownloadAsync(UpdateManifest update, string path, IProgress<double> progress, CancellationToken ct)
        {
            using HttpResponseMessage response = await http.GetAsync(
                new Uri(baseUrl, Uri.EscapeDataString(update.File)), HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = update.Size > 0 ? update.Size : response.Content.Headers.ContentLength ?? 0;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }

            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded game did not match its checksum.");
        }

        static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static void TryDeleteDir(string dir)
        {
            try { DeleteDir(dir); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        static void TryDelete(string file)
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
