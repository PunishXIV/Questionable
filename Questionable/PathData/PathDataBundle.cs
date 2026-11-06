using System.IO.Compression;
using System.Text.Json;

namespace Questionable.PathData;

internal static class PathDataBundle
{
    public const string ManifestEntryName = "manifest.json";

    public const string QuestPathPrefix = "QuestPaths/";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string GetDirectory(IDalamudPluginInterface pluginInterface) =>
        Path.Combine(pluginInterface.ConfigDirectory.FullName, "PathData");

    public static string GetBundlePath(IDalamudPluginInterface pluginInterface) =>
        Path.Combine(GetDirectory(pluginInterface), "bundle.zip");

    public static PathDataManifest? ReadManifest(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryName);
        if (entry == null)
            return null;

        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<PathDataManifest>(stream, ManifestJsonOptions);
    }
}
