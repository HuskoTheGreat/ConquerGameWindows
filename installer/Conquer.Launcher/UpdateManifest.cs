using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conquer.Launcher
{
    /// <summary>The latest.json that CI publishes next to each game build.</summary>
    public sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("file")] public string File { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }

        public static UpdateManifest Parse(string json) =>
            JsonSerializer.Deserialize(json, ManifestJson.Default.UpdateManifest);

        public string ToJson() => JsonSerializer.Serialize(this, ManifestJson.Default.UpdateManifest);
    }

    // Source-generated so the trimmed launcher needs no reflection.
    [JsonSerializable(typeof(UpdateManifest))]
    internal partial class ManifestJson : JsonSerializerContext { }
}
