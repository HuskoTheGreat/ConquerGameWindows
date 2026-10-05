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
    /// The manifest must carry a valid signature from the release key, and its build number must not go backwards.
    /// An update is downloaded, checked against the manifest's SHA-256 and size, unpacked next to the current copy and
    /// only then swapped in, so a failed or interrupted update always leaves the previous game playable.
    /// </summary>
    public sealed class Updater
    {
        public const string ManifestName = "update.json";

        /// <summary>No game build comes close; a manifest claiming more is refused rather than filling the disk.</summary>
        public const long MaxDownloadBytes = 1L << 30;
        public const string GameExe = "Conquer.exe";
        const string VersionFile = "version.json";

        readonly HttpClient http;
        readonly Uri baseUrl;
        readonly string root;
        readonly byte[] publicKey;

        /// <param name="publicKey">The release key's public half (SubjectPublicKeyInfo DER). Without it nothing installs.</param>
        public Updater(HttpClient http, Uri baseUrl, string root, byte[] publicKey)
        {
            this.http = http;
            this.baseUrl = baseUrl;
            this.root = root;
            this.publicKey = publicKey;
        }

        public string GameDir => Path.Combine(root, "game");
        public string GameExePath => Path.Combine(GameDir, GameExe);
        public bool HasGame => File.Exists(GameExePath);

        /// <summary>The installed build's version, or null when nothing is installed.</summary>
        public string InstalledVersion => Installed()?.Version;

        UpdateManifest Installed()
        {
            string path = Path.Combine(GameDir, VersionFile);
            if (!HasGame || !File.Exists(path)) return null;
            try { return UpdateManifest.Parse(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Returns the published manifest when it is newer than the installed build, otherwise null. Throws if it isn't
        /// signed by the release key or is malformed.
        /// </summary>
        public async Task<UpdateManifest> CheckAsync(CancellationToken ct)
        {
            byte[] json = await http.GetByteArrayAsync(new Uri(baseUrl, ManifestName), ct);
            UpdateManifest latest = SignedManifest.Verify(json, publicKey);
            if (latest == null || string.IsNullOrEmpty(latest.Version) || string.IsNullOrEmpty(latest.File)
                || string.IsNullOrEmpty(latest.Sha256) || latest.Build <= 0)
                throw new InvalidDataException("The update manifest is incomplete.");
            if (latest.File.Contains('/') || latest.File.Contains('\\') || latest.File.Contains(".."))
                throw new InvalidDataException("The update manifest names an invalid file.");
            if (latest.Size <= 0 || latest.Size > MaxDownloadBytes)
                throw new InvalidDataException("The update manifest has an invalid size.");

            UpdateManifest installed = Installed();
            if (installed == null) return latest;
            if (latest.Version == installed.Version) return null;
            // Never step back to an older build, even a correctly signed one: it may have a bug that was since fixed.
            return latest.Build > installed.Build ? latest : null;
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
            long total = update.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    if (done + read > total) throw new InvalidDataException("The download is larger than the update manifest says.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }

            if (new FileInfo(path).Length != total) throw new InvalidDataException("The download was cut short.");
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
