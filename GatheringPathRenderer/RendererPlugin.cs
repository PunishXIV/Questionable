using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GatheringPathRenderer.Windows;
using LLib.GameData;
using Pictomancy;
using Questionable.Model.Gathering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GatheringPathRenderer;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class RendererPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(nameof(RendererPlugin));
    private readonly List<uint> _colors = [0x40FF2020, 0x4020FF20, 0x402020FF, 0x40FFFF20, 0x40FF20FF, 0x4020FFFF];

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IClientState _clientState;
    private readonly IPluginLog _pluginLog;

    private readonly EditorCommands _editorCommands;
    private readonly EditorWindow _editorWindow;

    private readonly List<GatheringLocationContext> _gatheringLocations = [];
    private EClassJob _currentClassJob = EClassJob.Adventurer;

    public RendererPlugin(IDalamudPluginInterface pluginInterface, IClientState clientState,
        ICommandManager commandManager, IDataManager dataManager, ITargetManager targetManager, IChatGui chatGui,
        IObjectTable objectTable, IPluginLog pluginLog, IFramework framework)
    {
        _pluginInterface = pluginInterface;
        _clientState = clientState;
        _pluginLog = pluginLog;

        Configuration? configuration = (Configuration?)pluginInterface.GetPluginConfig();
        if (configuration == null)
        {
            configuration = new Configuration();
            pluginInterface.SavePluginConfig(configuration);
        }

        _editorCommands = new EditorCommands(this, dataManager, commandManager, targetManager, clientState, chatGui,
            configuration);
        var configWindow = new ConfigWindow(pluginInterface, configuration);
        _editorWindow = new EditorWindow(this, _editorCommands, dataManager, targetManager, clientState, objectTable,
                configWindow)
        { IsOpen = true };
        _windowSystem.AddWindow(configWindow);
        _windowSystem.AddWindow(_editorWindow);

        framework.RunOnFrameworkThread(() =>
        {
            _currentClassJob = (EClassJob?)_clientState.LocalPlayer?.ClassJob.RowId ?? EClassJob.Adventurer;
        });

        _pluginInterface.GetIpcSubscriber<object>("Questionable.ReloadData")
            .Subscribe(Reload);

        PictoService.Initialize(pluginInterface);
        LoadGatheringLocationsFromDirectory();

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw += Draw;
        _clientState.ClassJobChanged += ClassJobChanged;
    }

    internal DirectoryInfo PathsDirectory
    {
        get
        {
#if DEBUG
            DirectoryInfo? solutionDirectory = _pluginInterface.AssemblyLocation.Directory?.Parent?.Parent;
            if (solutionDirectory != null)
            {
                DirectoryInfo pathProjectDirectory =
                    new DirectoryInfo(Path.Combine(solutionDirectory.FullName, "GatheringPaths"));
                if (pathProjectDirectory.Exists)
                    return pathProjectDirectory;
            }

            throw new Exception($"Unable to resolve project path ({_pluginInterface.AssemblyLocation.Directory})");
#else
            var allPluginsDirectory =
 _pluginInterface.ConfigFile.Directory ?? throw new Exception("Unknown directory for plugin configs");
            return allPluginsDirectory
                .CreateSubdirectory("Questionable")
                .CreateSubdirectory("GatheringPaths");
#endif
        }
    }

    internal void Reload()
    {
        LoadGatheringLocationsFromDirectory();
    }

    private void LoadGatheringLocationsFromDirectory()
    {
        _gatheringLocations.Clear();

        try
        {
#if DEBUG
            foreach (var expansionFolder in Questionable.Model.ExpansionData.ExpansionFolders.Values)
                LoadFromDirectory(
                    new DirectoryInfo(Path.Combine(PathsDirectory.FullName, expansionFolder)));
            _pluginLog.Information(
                $"Loaded {_gatheringLocations.Count} gathering root locations from project directory");
#else
            LoadFromDirectory(PathsDirectory);
            _pluginLog.Information(
                $"Loaded {_gatheringLocations.Count} gathering root locations from {PathsDirectory.FullName} directory");
#endif
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to load paths from project directory");
        }
    }

    private void LoadFromDirectory(DirectoryInfo directory)
    {
        if (!directory.Exists)
            return;

        //_pluginLog.Information($"Loading locations from {directory}");
        foreach (FileInfo fileInfo in directory.GetFiles("*.json"))
        {
            try
            {
                using FileStream stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read);
                LoadLocationFromStream(fileInfo, stream);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Unable to load file {fileInfo.FullName}", e);
            }
        }

        foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            LoadFromDirectory(childDirectory);
    }

    private void LoadLocationFromStream(FileInfo fileInfo, Stream stream)
    {
        var locationNode = JsonNode.Parse(stream)!;
        GatheringRoot root = locationNode.Deserialize<GatheringRoot>()!;
        _gatheringLocations.Add(new GatheringLocationContext(fileInfo, ushort.Parse(fileInfo.Name.Split('_')[0]),
            root));
    }

    internal IEnumerable<GatheringLocationContext> GetLocationsInTerritory(ushort territoryId)
        => _gatheringLocations.Where(x => x.Root.Steps.LastOrDefault()?.TerritoryId == territoryId);

    internal void Save(FileInfo targetFile, GatheringRoot root)
    {
        JsonSerializerOptions options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { NoEmptyCollectionModifier }
            },
        };
        using (var stream = File.Create(targetFile.FullName))
        {
            var jsonNode = (JsonObject)JsonSerializer.SerializeToNode(root, options)!;
            var newNode = new JsonObject();
            newNode.Add("$schema",
                "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/GatheringPaths/gatheringlocation-v1.json");
            foreach (var (key, value) in jsonNode)
                newNode.Add(key, value?.DeepClone());

            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true
            });
            newNode.WriteTo(writer, options);
        }

        Reload();
    }

    private static void NoEmptyCollectionModifier(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
            {
                property.ShouldSerialize = (_, val) => val is ICollection { Count: > 0 };
            }
        }
    }

    private void ClassJobChanged(uint classJobId)
    {
        _currentClassJob = (EClassJob)classJobId;
    }

    private void Draw()
    {
        if (!_currentClassJob.IsGatherer())
            return;

        using var drawList = PictoService.Draw();
        if (drawList == null)
            return;

        Vector3 position = _clientState.LocalPlayer?.Position ?? Vector3.Zero;
        foreach (var location in GetLocationsInTerritory(_clientState.TerritoryType))
        {
            if (!location.Root.Groups.Any(gr =>
                    gr.Nodes.Any(
                        no => no.Locations.Any(
                            loc => Vector3.Distance(loc.Position, position) < 200f))))
                continue;

            foreach (var group in location.Root.Groups)
            {
                foreach (GatheringNode node in group.Nodes)
                {
                    foreach (var x in node.Locations)
                    {
                        bool isUnsaved = false;
                        bool isCone = false;
                        float minimumAngle = 0;
                        float maximumAngle = 0;
                        if (_editorWindow.TryGetOverride(x.InternalId, out LocationOverride? locationOverride) &&
                            locationOverride != null)
                        {
                            isUnsaved = locationOverride.NeedsSave();
                            if (locationOverride.IsCone())
                            {
                                isCone = true;
                                minimumAngle = locationOverride.MinimumAngle.GetValueOrDefault();
                                maximumAngle = locationOverride.MaximumAngle.GetValueOrDefault();
                            }
                        }

                        if (!isCone && x.IsCone())
                        {
                            isCone = true;
                            minimumAngle = x.MinimumAngle.GetValueOrDefault();
                            maximumAngle = x.MaximumAngle.GetValueOrDefault();
                        }

                        minimumAngle *= (float)Math.PI / 180;
                        maximumAngle *= (float)Math.PI / 180;
                        if (!isCone || maximumAngle - minimumAngle >= 2 * Math.PI)
                        {
                            minimumAngle = 0;
                            maximumAngle = (float)Math.PI * 2;
                        }

                        uint color = _colors[location.Root.Groups.IndexOf(group) % _colors.Count];
                        drawList.AddFanFilled(x.Position,
                            locationOverride?.MinimumDistance ?? x.CalculateMinimumDistance(),
                            locationOverride?.MaximumDistance ?? x.CalculateMaximumDistance(),
                            minimumAngle, maximumAngle, color);
                        drawList.AddFan(x.Position,
                            locationOverride?.MinimumDistance ?? x.CalculateMinimumDistance(),
                            locationOverride?.MaximumDistance ?? x.CalculateMaximumDistance(),
                            minimumAngle, maximumAngle, color | 0xFF000000);

                        drawList.AddText(x.Position, isUnsaved ? 0xFFFF0000 : 0xFFFFFFFF, $"{location.Root.Groups.IndexOf(group)} // {node.DataId} / {node.Locations.IndexOf(x)} || {minimumAngle}, {maximumAngle}", 1f);
#if false
                        var a = GatheringMath.CalculateLandingLocation(x, 0, 0);
                        var b = GatheringMath.CalculateLandingLocation(x, 1, 1);
                        new Element(ElementType.CircleAtFixedCoordinates)
                        {
                            refX = a.X,
                            refY = a.Z,
                            refZ = a.Y,
                            color = _colors[0],
                            radius = 0.1f,
                            Enabled = true,
                            overlayText = "Min Angle"
                        },
                        new Element(ElementType.CircleAtFixedCoordinates)
                        {
                            refX = b.X,
                            refY = b.Z,
                            refZ = b.Y,
                            color = _colors[1],
                            radius = 0.1f,
                            Enabled = true,
                            overlayText = "Max Angle"
                        }
#endif
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _clientState.ClassJobChanged -= ClassJobChanged;
        _pluginInterface.UiBuilder.Draw -= Draw;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;

        PictoService.Dispose();

        _pluginInterface.GetIpcSubscriber<object>("Questionable.ReloadData")
            .Unsubscribe(Reload);

        _editorCommands.Dispose();
    }

    internal sealed record GatheringLocationContext(FileInfo File, ushort Id, GatheringRoot Root);
}
