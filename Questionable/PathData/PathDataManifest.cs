using System;
using System.Text.Json.Serialization;

namespace Questionable.PathData;

/// <summary>
///     Metadata describing a published quest/gathering path bundle. A copy is stored as the
///     <c>manifest.json</c> entry inside every bundle zip, and a standalone copy is published
///     remotely so the updater can check for a newer bundle without downloading the whole zip.
/// </summary>
internal sealed class PathDataManifest
{
    /// <summary>Version of this manifest's own format.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>
    ///     Monotonically increasing identity of the path-data snapshot. Independent of the
    ///     plugin's release version.
    /// </summary>
    [JsonPropertyName("dataVersion")]
    public long DataVersion { get; set; }

    /// <summary>
    ///     Lowest <see cref="Questionable.Model.PathDataFormat.CurrentVersion" /> a plugin must
    ///     have to safely load this bundle.
    /// </summary>
    [JsonPropertyName("minPluginDataFormat")]
    public int MinPluginDataFormat { get; set; }

    /// <summary>
    ///     Optional highest <see cref="Questionable.Model.PathDataFormat.CurrentVersion" /> this
    ///     bundle supports; <c>null</c> means no upper bound.
    /// </summary>
    [JsonPropertyName("maxPluginDataFormat")]
    public int? MaxPluginDataFormat { get; set; }

    /// <summary>URL the bundle zip can be downloaded from (used by the remote manifest only).</summary>
    [JsonPropertyName("bundleUrl")]
    public string? BundleUrl { get; set; }

    /// <summary>Lower-case hex SHA-256 of the bundle zip (used by the remote manifest only).</summary>
    [JsonPropertyName("bundleSha256")]
    public string? BundleSha256 { get; set; }

    /// <summary>When the bundle was produced.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>
    ///     Whether a plugin reporting the given <paramref name="pluginDataFormat" /> can safely
    ///     load this bundle (Gate A).
    /// </summary>
    public bool IsCompatibleWith(int pluginDataFormat) =>
        MinPluginDataFormat <= pluginDataFormat &&
        (MaxPluginDataFormat == null || pluginDataFormat <= MaxPluginDataFormat.Value);
}
