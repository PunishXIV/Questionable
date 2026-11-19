using System.IO.Compression;
using System.Text.Json;

namespace Questionable.PathData;

/// <summary>
///     Shared constants and helpers for the downloaded path-data bundle — a single zip,
///     stored under <c>{ConfigDirectory}/PathData/</c>, that overrides the path data compiled
///     into the plugin.
/// </summary>
internal static class PathDataBundle
{
    /// <summary>Name of the manifest entry inside the bundle zip.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>Zip-entry path prefix for quest path files.</summary>
    public const string QuestPathPrefix = "QuestPaths/";

    /// <summary>Zip-entry path prefix for gathering path files.</summary>
    public const string GatheringPathPrefix = "GatheringPaths/";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The <c>PathData</c> directory holding the downloaded bundle.</summary>
    public static string GetDirectory(IDalamudPluginInterface pluginInterface) =>
        Path.Combine(pluginInterface.ConfigDirectory.FullName, "PathData");

    /// <summary>Full path of the downloaded bundle zip (may not exist).</summary>
    public static string GetBundlePath(IDalamudPluginInterface pluginInterface) =>
        Path.Combine(GetDirectory(pluginInterface), "bundle.zip");

    /// <summary>
    ///     Reads the <c>manifest.json</c> entry from an open bundle archive, or <c>null</c> if it
    ///     is missing or unreadable.
    /// </summary>
    public static PathDataManifest? ReadManifest(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryName);
        if (entry == null)
            return null;

        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<PathDataManifest>(stream, ManifestJsonOptions);
    }
}
