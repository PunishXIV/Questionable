using System.Text.Json.Serialization;

namespace Questionable.PathData;

internal sealed class PathDataManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("dataVersion")]
    public long DataVersion { get; set; }

    [JsonPropertyName("minPluginDataFormat")]
    public int MinPluginDataFormat { get; set; }

    [JsonPropertyName("maxPluginDataFormat")]
    public int? MaxPluginDataFormat { get; set; }

    [JsonPropertyName("bundleUrl")]
    public string? BundleUrl { get; set; }

    [JsonPropertyName("bundleSha256")]
    public string? BundleSha256 { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt { get; set; }

    public bool IsCompatibleWith(int pluginDataFormat) =>
        MinPluginDataFormat <= pluginDataFormat &&
        (MaxPluginDataFormat == null || pluginDataFormat <= MaxPluginDataFormat.Value);
}
