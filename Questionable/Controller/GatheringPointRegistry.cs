using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using ECommons.ExcelServices;
using Questionable.GatheringPaths;
using Questionable.Model.Gathering;
namespace Questionable.Controller;

internal sealed class GatheringPointRegistry : IDisposable
{
    private readonly GatheringData _gatheringData;

    private readonly Dictionary<GatheringPointId, GatheringRoot> _gatheringPoints = [];
    private readonly ILogger<QuestRegistry> _logger;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestRegistry _questRegistry;
    private readonly Configuration _configuration;

    public GatheringPointRegistry(IDalamudPluginInterface pluginInterface,
        QuestRegistry questRegistry,
        GatheringData gatheringData,
        Configuration configuration,
        ILogger<QuestRegistry> logger)
    {
        _pluginInterface = pluginInterface;
        _questRegistry = questRegistry;
        _gatheringData = gatheringData;
        _logger = logger;
        _configuration = configuration;

        _questRegistry.Reloaded += OnReloaded;
    }

    public void Dispose() => _questRegistry.Reloaded -= OnReloaded;

    private void OnReloaded(object? sender, EventArgs e) => Reload();

    public void Reload()
    {
        _gatheringPoints.Clear();

        if (!LoadGatheringPointsFromDownloadedBundle())
            //LoadGatheringPointsFromAssembly();
            _logger.LogWarning("Bundled gathering points were not loaded, we have no gathering points!");
        if (_configuration.Advanced.Debug || Svc.PluginInterface.IsDev
#if DEBUG
        || true
#endif
        )
            LoadGatheringPointsFromProjectDirectory();

        try
        {
            LoadFromDirectory(new(Path.Combine(_pluginInterface.ConfigDirectory.FullName, "GatheringPoints")));
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                "Failed to load gathering points from user directory (some may have been successfully loaded)");
        }

        _logger.LogInformation("Loaded {Count} gathering points in total", _gatheringPoints.Count);
    }

    private void LoadGatheringPointsFromAssembly()
    {
        _logger.LogInformation("Loading gathering points from assembly");

        foreach ((ushort gatheringPointId, GatheringRoot gatheringRoot) in AssemblyGatheringLocationLoader.Locations)
        {
            if (gatheringRoot.Steps.Count >= 1)
            {
                foreach (GatheringNodeGroup group in gatheringRoot.Groups)
                {
                    foreach (GatheringNode node in group.Nodes)
                    {
                        foreach (GatheringLocation position in node.Locations)
                        {
                            gatheringRoot.Steps[0].Position = gatheringRoot.Steps[0].Position ?? position.Position;
                            gatheringRoot.Steps[0].Fly = gatheringRoot.Steps[0].Fly ?? true;
                            break;
                        }

                        break;
                    }

                    break;
                }
            }

            _gatheringPoints[new(gatheringPointId)] = gatheringRoot;
        }

        _logger.LogInformation("Loaded {Count} gathering points from assembly", _gatheringPoints.Count);
    }

    private void LoadGatheringPointsFromProjectDirectory()
    {
        DirectoryInfo? solutionDirectory = _pluginInterface.AssemblyLocation.Directory?.Parent?.Parent;
        if (solutionDirectory != null)
        {
            DirectoryInfo pathProjectDirectory = new(Path.Combine(solutionDirectory.FullName, "GatheringPaths"));
            if (pathProjectDirectory.Exists)
            {
                try
                {
                    foreach (string expansionFolder in ExpansionData.ExpansionFolders.Values)
                    {
                        LoadFromDirectory(
                            new(Path.Combine(pathProjectDirectory.FullName, expansionFolder)));
                    }
                }
                catch (Exception e)
                {
                    _gatheringPoints.Clear();
                    _logger.LogError(e, "Failed to load gathering points from project directory");
                }
            }
        }
    }

    /// <summary>
    ///     Loads gathering points from a downloaded path bundle
    ///     (<c>{ConfigDirectory}/PathData/bundle.zip</c>) if one is present. Entries here override
    ///     the compiled baseline but are themselves overridden by the hand-authored user
    ///     directory. A single bad entry is skipped rather than aborting the rest of the bundle.
    /// </summary>
    private bool LoadGatheringPointsFromDownloadedBundle()
    {
        string bundlePath = PathDataBundle.GetBundlePath(_pluginInterface);
        if (!File.Exists(bundlePath))
            return false;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(bundlePath);
            PathDataManifest? manifest = PathDataBundle.ReadManifest(archive);

            // Gate A: skip a bundle missing its manifest or requiring a newer plugin.
            // QuestRegistry already logs the detailed reason during its own load pass.
            if (manifest == null || !manifest.IsCompatibleWith(PathDataFormat.CurrentVersion))
                return false;

            int loaded = 0, failed = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(PathDataBundle.GatheringPathPrefix, StringComparison.Ordinal) ||
                    !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using Stream stream = entry.Open();
                    LoadGatheringPointFromStream(entry.Name, stream);
                    ++loaded;
                }
                catch (Exception e)
                {
                    ++failed;
                    _logger.LogWarning(e, "Failed to load gathering point '{Entry}' from downloaded bundle (skipped)",
                        entry.FullName);
                }
            }

            _logger.LogInformation("Loaded {Loaded} gathering points from downloaded path bundle{Failed}",
                loaded, failed > 0 ? $", {failed} skipped" : string.Empty);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load gathering points from downloaded path bundle");
            return false;
        }
        return true;
    }

    private void LoadGatheringPointFromStream(string fileName, Stream stream)
    {
        GatheringPointId? gatheringPointId = ExtractGatheringPointIdFromName(fileName);
        if (gatheringPointId == null)
            return;

        GatheringRoot gatheringRoot = JsonSerializer.Deserialize<GatheringRoot>(stream)!;
        if (gatheringRoot.Steps.Count >= 1)
        {
            foreach (GatheringNodeGroup group in gatheringRoot.Groups)
            {
                foreach (GatheringNode node in group.Nodes)
                {
                    foreach (GatheringLocation position in node.Locations)
                    {
                        gatheringRoot.Steps[0].Position = gatheringRoot.Steps[0].Position ?? position.Position;
                        gatheringRoot.Steps[0].Fly = gatheringRoot.Steps[0].Fly ?? true;
                        break;
                    }

                    break;
                }

                break;
            }
        }

        _gatheringPoints[gatheringPointId] = gatheringRoot;
    }

    private void LoadFromDirectory(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            _logger.LogInformation("Not loading gathering points from {DirectoryName} (doesn't exist)", directory);
            return;
        }

        foreach (FileInfo fileInfo in directory.GetFiles("*.json"))
        {
            try
            {
                using FileStream stream = new(fileInfo.FullName, FileMode.Open, FileAccess.Read);
                LoadGatheringPointFromStream(fileInfo.Name, stream);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Unable to load file {fileInfo.FullName}", e);
            }
        }

        foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            LoadFromDirectory(childDirectory);
    }

    private static GatheringPointId? ExtractGatheringPointIdFromName(string resourceName)
    {
        string name = resourceName.Substring(0, resourceName.Length - ".json".Length);
        name = name.Substring(name.LastIndexOf('.') + 1);

        if (!name.Contains('_', StringComparison.Ordinal))
            return null;

        string[] parts = name.Split('_', 2);
        return GatheringPointId.FromString(parts[0]);
    }

    public bool TryGetGatheringPoint(GatheringPointId gatheringPointId, [NotNullWhen(true)] out GatheringRoot? gatheringRoot) =>
        _gatheringPoints.TryGetValue(gatheringPointId, out gatheringRoot);

    public bool TryGetGatheringPointId(uint itemId, Job classJobId,
        [NotNullWhen(true)] out GatheringPointId? gatheringPointId)
    {
        if (classJobId == Job.MIN)
        {
            if (_gatheringData.TryGetMinerGatheringPointByItemId(itemId, out gatheringPointId))
                return true;

            gatheringPointId = _gatheringPoints
                .Where(x => x.Value.ExtraQuestItems.Contains(itemId))
                .Select(x => x.Key)
                .FirstOrDefault(x => _gatheringData.MinerGatheringPoints.Contains(x));
            return gatheringPointId != null;
        }

        if (classJobId == Job.BTN)
        {
            if (_gatheringData.TryGetBotanistGatheringPointByItemId(itemId, out gatheringPointId))
                return true;

            gatheringPointId = _gatheringPoints
                .Where(x => x.Value.ExtraQuestItems.Contains(itemId))
                .Select(x => x.Key)
                .FirstOrDefault(x => _gatheringData.BotanistGatheringPoints.Contains(x));
            return gatheringPointId != null;
        }

        gatheringPointId = null;
        return false;
    }
}
