using Dalamud.Plugin;
using LLib.GameData;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.GatheringPaths;
using Questionable.Model;
using Questionable.Model.Gathering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Questionable.Controller;

internal sealed class GatheringPointRegistry : IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestRegistry _questRegistry;
    private readonly GatheringData _gatheringData;
    private readonly ILogger<QuestRegistry> _logger;

    private readonly Dictionary<GatheringPointId, GatheringRoot> _gatheringPoints = new();

    public GatheringPointRegistry(IDalamudPluginInterface pluginInterface,
        QuestRegistry questRegistry,
        GatheringData gatheringData,
        ILogger<QuestRegistry> logger)
    {
        _pluginInterface = pluginInterface;
        _questRegistry = questRegistry;
        _gatheringData = gatheringData;
        _logger = logger;

        _questRegistry.Reloaded += OnReloaded;
    }

    private void OnReloaded(object? sender, EventArgs e) => Reload();

    public void Reload()
    {
        _gatheringPoints.Clear();

        LoadGatheringPointsFromAssembly();
        LoadGatheringPointsFromProjectDirectory();

        try
        {
            LoadFromDirectory(new DirectoryInfo(Path.Combine(_pluginInterface.ConfigDirectory.FullName, "GatheringPoints")));
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                "Failed to load gathering points from user directory (some may have been successfully loaded)");
        }

        _logger.LogInformation("Loaded {Count} gathering points in total", _gatheringPoints.Count);
    }

    [Conditional("RELEASE")]
    private void LoadGatheringPointsFromAssembly()
    {
        _logger.LogInformation("Loading gathering points from assembly");

        foreach ((ushort gatheringPointId, GatheringRoot gatheringRoot) in
                 AssemblyGatheringLocationLoader.GetLocations())
        {
            _gatheringPoints[new GatheringPointId(gatheringPointId)] = gatheringRoot;
        }

        _logger.LogInformation("Loaded {Count} gathering points from assembly", _gatheringPoints.Count);
    }

    [Conditional("DEBUG")]
    private void LoadGatheringPointsFromProjectDirectory()
    {
        DirectoryInfo? solutionDirectory = _pluginInterface.AssemblyLocation.Directory?.Parent?.Parent;
        if (solutionDirectory != null)
        {
            DirectoryInfo pathProjectDirectory =
                new DirectoryInfo(Path.Combine(solutionDirectory.FullName, "GatheringPaths"));
            if (pathProjectDirectory.Exists)
            {
                try
                {
                    foreach (var expansionFolder in ExpansionData.ExpansionFolders.Values)
                        LoadFromDirectory(
                            new DirectoryInfo(Path.Combine(pathProjectDirectory.FullName, expansionFolder)));
                }
                catch (Exception e)
                {
                    _gatheringPoints.Clear();
                    _logger.LogError(e, "Failed to load gathering points from project directory");
                }
            }
        }
    }

    private void LoadGatheringPointFromStream(string fileName, Stream stream)
    {
        //_logger.LogTrace("Loading gathering point from '{FileName}'", fileName);
        GatheringPointId? gatheringPointId = ExtractGatheringPointIdFromName(fileName);
        if (gatheringPointId == null)
            return;

        _gatheringPoints[gatheringPointId] = JsonSerializer.Deserialize<GatheringRoot>(stream)!;
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
                using FileStream stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read);
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

    public bool TryGetGatheringPoint(GatheringPointId gatheringPointId, [NotNullWhen(true)] out GatheringRoot? gatheringRoot)
        => _gatheringPoints.TryGetValue(gatheringPointId, out gatheringRoot);

    public bool TryGetGatheringPointId(uint itemId, EClassJob classJobId,
        [NotNullWhen(true)] out GatheringPointId? gatheringPointId)
    {
        if (classJobId == EClassJob.Miner)
        {
            if (_gatheringData.TryGetMinerGatheringPointByItemId(itemId, out gatheringPointId))
                return true;

            gatheringPointId = _gatheringPoints
                .Where(x => x.Value.ExtraQuestItems.Contains(itemId))
                .Select(x => x.Key)
                .FirstOrDefault(x => _gatheringData.MinerGatheringPoints.Contains(x));
            return gatheringPointId != null;
        }
        else if (classJobId == EClassJob.Botanist)
        {
            if (_gatheringData.TryGetBotanistGatheringPointByItemId(itemId, out gatheringPointId))
                return true;

            gatheringPointId = _gatheringPoints
                .Where(x => x.Value.ExtraQuestItems.Contains(itemId))
                .Select(x => x.Key)
                .FirstOrDefault(x => _gatheringData.BotanistGatheringPoints.Contains(x));
            return gatheringPointId != null;
        }
        else
        {
            gatheringPointId = null;
            return false;
        }
    }

    public void Dispose()
    {
        _questRegistry.Reloaded -= OnReloaded;
    }
}
