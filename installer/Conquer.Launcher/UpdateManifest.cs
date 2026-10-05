using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conquer.Launcher
{
    /// <summary>What CI publishes about each game build. It reaches the launcher inside a <see cref="SignedManifest"/>.</summary>
    public sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; }

        /// <summary>Increases with every published build, so an older build can't be pushed back out.</summary>
        [JsonPropertyName("build")] public long Build { get; set; }

        [JsonPropertyName("file")] public string File { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }

        public static UpdateManifest Parse(string json) =>
            JsonSerializer.Deserialize(json, ManifestJson.Default.UpdateManifest);

        public static UpdateManifest Parse(byte[] json) =>
            JsonSerializer.Deserialize(json, ManifestJson.Default.UpdateManifest);

        public string ToJson() => JsonSerializer.Serialize(this, ManifestJson.Default.UpdateManifest);
    }

    /// <summary>
    /// update.json: the manifest's exact bytes plus an ECDSA P-256 / SHA-256 signature over them. The private key
    /// never lives on GitHub, so someone who can only write to the release can't publish a build players will run.
    /// Manifest and signature travel in one file, so a launcher can never pair a new manifest with an old signature.
    /// </summary>
    public sealed class SignedManifest
    {
        [JsonPropertyName("manifest")] public string Manifest { get; set; }
        [JsonPropertyName("signature")] public string Signature { get; set; }

        public static SignedManifest Sign(byte[] manifest, ECDsa key) => new SignedManifest
        {
            Manifest = Convert.ToBase64String(manifest),
            Signature = Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256)),
        };

        /// <summary>Returns the manifest if the signature checks out against <paramref name="publicKey"/>; throws otherwise.</summary>
        public static UpdateManifest Verify(byte[] json, byte[] publicKey)
        {
            if (publicKey == null || publicKey.Length == 0)
                throw new InvalidDataException("This launcher has no update key, so it can't check updates.");

            SignedManifest signed;
            byte[] manifest, signature;
            try
            {
                signed = JsonSerializer.Deserialize(json, ManifestJson.Default.SignedManifest);
                manifest = Convert.FromBase64String(signed?.Manifest ?? "");
                signature = Convert.FromBase64String(signed?.Signature ?? "");
            }
            catch (Exception e) when (e is JsonException || e is FormatException)
            {
                throw new InvalidDataException("The update manifest is unreadable.", e);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (manifest.Length == 0 || !ecdsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256))
                throw new InvalidDataException("The update isn't signed by the Conquer release key.");

            try
            {
                return UpdateManifest.Parse(manifest);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("The update manifest is unreadable.", e);
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, ManifestJson.Default.SignedManifest);
    }

    // Source-generated so the trimmed launcher needs no reflection.
    [JsonSerializable(typeof(UpdateManifest))]
    [JsonSerializable(typeof(SignedManifest))]
    internal partial class ManifestJson : JsonSerializerContext { }
}
