using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Conquer.Launcher.Tests
{
    public class UpdaterTests
    {
        static readonly Uri BaseUrl = new Uri("https://example.test/releases/download/game-latest/");

        string root;
        FakeServer server;
        Updater updater;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "conquer-launcher-" + Guid.NewGuid().ToString("N"));
            server = new FakeServer();
            updater = new Updater(new HttpClient(server), BaseUrl, root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public async Task FreshInstall_DownloadsAndUnpacksTheGame()
        {
            Publish("1-aaa", ("Conquer.exe", "v1"), ("Conquer.dll", "v1"));
            Assert.IsFalse(updater.HasGame);

            UpdateManifest latest = await updater.CheckAsync(CancellationToken.None);
            Assert.AreEqual("1-aaa", latest.Version);

            double last = 0;
            await updater.InstallAsync(latest, new Inline(p => last = p), CancellationToken.None);

            Assert.IsTrue(updater.HasGame);
            Assert.AreEqual("1-aaa", updater.InstalledVersion);
            Assert.AreEqual("v1", File.ReadAllText(Path.Combine(updater.GameDir, "Conquer.dll")));
            Assert.AreEqual(1.0, last, 1e-9);
            CollectionAssert.AreEquivalent(new[] { updater.GameDir }, Directory.GetDirectories(root));
            CollectionAssert.IsEmpty(Directory.GetFiles(root));
        }

        [Test]
        public async Task UpToDate_CheckReturnsNull()
        {
            await InstallLatest("1-aaa", ("Conquer.exe", "v1"));
            Assert.IsNull(await updater.CheckAsync(CancellationToken.None));
        }

        [Test]
        public async Task NewPush_ReplacesTheGameAndDropsStaleFiles()
        {
            await InstallLatest("1-aaa", ("Conquer.exe", "v1"), ("Old.dll", "v1"));
            await InstallLatest("2-bbb", ("Conquer.exe", "v2"), ("New.dll", "v2"));

            Assert.AreEqual("2-bbb", updater.InstalledVersion);
            Assert.AreEqual("v2", File.ReadAllText(updater.GameExePath));
            Assert.IsFalse(File.Exists(Path.Combine(updater.GameDir, "Old.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(updater.GameDir, "New.dll")));
        }

        [Test]
        public async Task ChecksumMismatch_KeepsThePreviousGame()
        {
            await InstallLatest("1-aaa", ("Conquer.exe", "v1"));
            Publish("2-bbb", ("Conquer.exe", "v2"));
            server.Files["Conquer-win-x64.zip"] = Zip(("Conquer.exe", "tampered"));

            UpdateManifest latest = await updater.CheckAsync(CancellationToken.None);
            Assert.ThrowsAsync<InvalidDataException>(() => updater.InstallAsync(latest, null, CancellationToken.None));

            Assert.AreEqual("1-aaa", updater.InstalledVersion);
            Assert.AreEqual("v1", File.ReadAllText(updater.GameExePath));
        }

        [Test]
        public async Task BuildWithoutTheGame_IsRejected()
        {
            await InstallLatest("1-aaa", ("Conquer.exe", "v1"));
            Publish("2-bbb", ("Readme.txt", "oops"));

            UpdateManifest latest = await updater.CheckAsync(CancellationToken.None);
            Assert.ThrowsAsync<InvalidDataException>(() => updater.InstallAsync(latest, null, CancellationToken.None));
            Assert.AreEqual("1-aaa", updater.InstalledVersion);
        }

        [Test]
        public void ManifestPointingOutsideTheReleaseFolder_IsRejected()
        {
            server.Files[Updater.ManifestName] = Encoding(new UpdateManifest
                { Version = "3", File = "../../evil.zip", Sha256 = "00" }.ToJson());
            Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync(CancellationToken.None));
        }

        [Test]
        public void Offline_CheckThrows()
        {
            Assert.ThrowsAsync<HttpRequestException>(() => updater.CheckAsync(CancellationToken.None));
        }

        async Task InstallLatest(string version, params (string Name, string Text)[] files)
        {
            Publish(version, files);
            await updater.InstallAsync(await updater.CheckAsync(CancellationToken.None), null, CancellationToken.None);
        }

        void Publish(string version, params (string Name, string Text)[] files)
        {
            byte[] zip = Zip(files);
            server.Files["Conquer-win-x64.zip"] = zip;
            server.Files[Updater.ManifestName] = Encoding(new UpdateManifest
            {
                Version = version,
                File = "Conquer-win-x64.zip",
                Sha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
                Size = zip.Length,
            }.ToJson());
        }

        static byte[] Zip(params (string Name, string Text)[] files)
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var (name, text) in files)
                    using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
                        writer.Write(text);
            return ms.ToArray();
        }

        static byte[] Encoding(string text) => System.Text.Encoding.UTF8.GetBytes(text);

        sealed class FakeServer : HttpMessageHandler
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string name = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Substring(BaseUrl.AbsolutePath.Length));
                if (Files.Count == 0) throw new HttpRequestException("offline");
                return Task.FromResult(Files.TryGetValue(name, out byte[] body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        sealed class Inline : IProgress<double>
        {
            readonly Action<double> report;
            public Inline(Action<double> report) => this.report = report;
            public void Report(double value) => report(value);
        }
    }
}
