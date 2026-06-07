using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatheringPathRenderer.Windows;
using Pictomancy;
using Questionable.Model;
using Questionable.Model.Gathering;
namespace GatheringPathRenderer;

public sealed class RendererPlugin : IDalamudPlugin
{
    private readonly IClientState _clientState;
    private readonly List<uint> _colors = [0x40FF2020, 0x4020FF20, 0x402020FF, 0x40FFFF20, 0x40FF20FF, 0x4020FFFF];

    private readonly EditorCommands _editorCommands;
    private readonly EditorWindow _editorWindow;

    private readonly IObjectTable _objectTable;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;
    private readonly WindowSystem _windowSystem = new(nameof(RendererPlugin));
    private Job _currentClassJob = Job.ADV;

    public RendererPlugin(IDalamudPluginInterface pluginInterface, IClientState clientState,
        ICommandManager commandManager, IDataManager dataManager, ITargetManager targetManager, IChatGui chatGui,
        IObjectTable objectTable, IPluginLog pluginLog, IFramework framework)
    {
        _pluginInterface = pluginInterface;
        _clientState = clientState;
        _objectTable = objectTable;
        _pluginLog = pluginLog;
        GBRLocationData = LoadGBRPosData(_pluginInterface.AssemblyLocation.DirectoryName!);
        pluginLog.Info($"Loaded {GBRLocationData.Count} entries from GBR data");
        ECommonsMain.Init(pluginInterface, this);

        Configuration? configuration = (Configuration?)pluginInterface.GetPluginConfig();
        if (configuration == null)
        {
            configuration = new();
            pluginInterface.SavePluginConfig(configuration);
        }

        _editorCommands = new(this, dataManager, commandManager, targetManager, clientState,
            objectTable, chatGui, pluginLog, configuration);
        ConfigWindow configWindow = new(pluginInterface, configuration);
        _editorWindow = new(this, _editorCommands, dataManager, commandManager, targetManager, clientState, objectTable,
                configWindow)
        { IsOpen = true };
        _windowSystem.AddWindow(configWindow);
        _windowSystem.AddWindow(_editorWindow);

        framework.RunOnFrameworkThread(() =>
        {
            unsafe
            {
                _currentClassJob = (Job?)PlayerState.Instance()->CurrentClassJobId ?? Job.ADV;
            }
        });

        _pluginInterface.GetIpcSubscriber<object>("Questionable.ReloadData")
            .Subscribe(Reload);

        PctService.Initialize(pluginInterface);
        LoadGatheringLocationsFromDirectory();

        _pluginInterface.UiBuilder.Draw += Draw;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _clientState.ClassJobChanged += ClassJobChanged;
    }

    internal List<GatheringLocationContext> GatheringLocations { get; } =
        [];

    internal Dictionary<uint, List<Vector3>> GBRLocationData { get; }

    internal bool DistantRange { get; set; }

    internal DirectoryInfo PathsDirectory
    {
        get
        {
#if DEBUG
            DirectoryInfo? solutionDirectory = _pluginInterface.AssemblyLocation.Directory?.Parent?.Parent;
            if (solutionDirectory != null)
            {
                DirectoryInfo pathProjectDirectory =
                    new(Path.Combine(solutionDirectory.FullName, "GatheringPaths"));
                if (pathProjectDirectory.Exists)
                    return pathProjectDirectory;
            }

            throw new($"Unable to resolve project path ({_pluginInterface.AssemblyLocation.Directory})");
#else
            var allPluginsDirectory =
                _pluginInterface.ConfigFile.Directory ?? throw new Exception("Unknown directory for plugin configs");
            return allPluginsDirectory
                .CreateSubdirectory("Questionable")
                .CreateSubdirectory("GatheringPaths");
#endif
        }
    }

    public void Dispose()
    {
        _clientState.ClassJobChanged -= ClassJobChanged;
        _pluginInterface.UiBuilder.Draw -= Draw;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();

        PctService.Dispose();

        _pluginInterface.GetIpcSubscriber<object>("Questionable.ReloadData")
            .Unsubscribe(Reload);

        _editorCommands.Dispose();
    }

    internal void Reload() => LoadGatheringLocationsFromDirectory();

    private void LoadGatheringLocationsFromDirectory()
    {
        GatheringLocations.Clear();

        try
        {
#if DEBUG
            foreach (string expansionFolder in ExpansionData.ExpansionFolders.Values)
                LoadFromDirectory(
                    new(Path.Combine(PathsDirectory.FullName, expansionFolder)));
            _pluginLog.Information(
                $"Loaded {GatheringLocations.Count} gathering root locations from project directory");
#else
            LoadFromDirectory(PathsDirectory);
            _pluginLog.Information(
                $"Loaded {GatheringLocations.Count} gathering root locations from {PathsDirectory.FullName} directory");
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
                using FileStream stream = new(fileInfo.FullName, FileMode.Open, FileAccess.Read);
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
        JsonNode locationNode = JsonNode.Parse(stream)!;
        GatheringRoot root = locationNode.Deserialize<GatheringRoot>()!;
        GatheringLocations.Add(new(fileInfo, ushort.Parse(fileInfo.Name.Split('_')[0], CultureInfo.InvariantCulture),
            root));
    }

    public static Dictionary<uint, List<Vector3>> LoadGBRPosData(string directoryName)
    {
        Stream stream =
                typeof(RendererPlugin).Assembly.GetManifestResourceStream(
                    "GatheringPathRenderer.GBRWorldLocations") ??
                throw new InvalidOperationException($"world_locations.json was not found");
        JsonNode? root = JsonNode.Parse(stream);
        Dictionary<uint, List<Vector3>> result = new();

        if (root is not JsonObject obj)
            return result;

        foreach (KeyValuePair<string, JsonNode?> kvp in obj)
        {
            if (!uint.TryParse(kvp.Key, out uint key))
                continue;

            List<Vector3> vectorList = new();
            if (kvp.Value is JsonArray arr)
            {
                foreach (JsonNode? vecNode in arr)
                {
                    float x = vecNode?["X"]?.GetValue<float>() ?? 0f;
                    float y = vecNode?["Y"]?.GetValue<float>() ?? 0f;
                    float z = vecNode?["Z"]?.GetValue<float>() ?? 0f;
                    vectorList.Add(new(x, y, z));
                }
            }

            result[key] = vectorList;
        }

        return result;
    }

    internal IEnumerable<GatheringLocationContext> GetLocationsInTerritory(uint territoryId)
        => GatheringLocations.Where(x => x.Root.Steps.LastOrDefault()?.TerritoryId == territoryId);

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
            }
        };
        using (FileStream stream = File.Create(targetFile.FullName))
        {
            JsonObject jsonNode = (JsonObject)JsonSerializer.SerializeToNode(root, options)!;
            JsonObject newNode = new()
            {
                {
                    "$schema",
                    "https://qstxiv.github.io/schema/gatheringlocation-v1.json"
                }
            };
            foreach ((string key, JsonNode? value) in jsonNode)
                newNode.Add(key, value?.DeepClone());

            using Utf8JsonWriter writer = new(stream, new()
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
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, val) => val is ICollection { Count: > 0 };
        }
    }

    private void ClassJobChanged(uint classJobId) => _currentClassJob = (Job)classJobId;

    private void Draw()
    {
        if (!_currentClassJob.IsDol())
            return;

        using PctDrawList? drawList = PctService.Draw();
        if (drawList == null)
            return;

        Vector3 position = _objectTable[0]?.Position ?? Vector3.Zero;
        float drawDistance = DistantRange ? 20000f : 200f;
        foreach (GatheringLocationContext location in GetLocationsInTerritory(_clientState.TerritoryType))
        {
            if (!location.Root.Groups.Any(gr =>
                gr.Nodes.Any(
                    no => no.Locations.Any(
                        loc => Vector3.Distance(loc.Position, position) < drawDistance))))
                continue;

            foreach (GatheringNodeGroup group in location.Root.Groups)
            {
                foreach (GatheringNode node in group.Nodes)
                {
                    foreach (GatheringLocation x in node.Locations)
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
                        uint color = _colors[location.Root.Groups.IndexOf(group) % _colors.Count];
                        if (!isCone || maximumAngle - minimumAngle >= 2 * Math.PI)
                        {
                            minimumAngle = 0;
                            maximumAngle = (float)Math.PI * 2;
                            color = ImGuiColors.DalamudOrange.ToUint() - 0xB0000000;
                        }

                        drawList.AddFanFilled(x.Position,
                            locationOverride?.MinimumDistance ?? x.CalculateMinimumDistance(),
                            locationOverride?.MaximumDistance ?? x.CalculateMaximumDistance(),
                            minimumAngle, maximumAngle, color);
                        drawList.AddFan(x.Position,
                            locationOverride?.MinimumDistance ?? x.CalculateMinimumDistance(),
                            locationOverride?.MaximumDistance ?? x.CalculateMaximumDistance(),
                            minimumAngle, maximumAngle, color | 0xFF000000);

                        drawList.AddText(x.Position, isUnsaved ? 0xFFFF0000 : 0xFFFFFFFF, $"{location.Root.Groups.IndexOf(group)} // {node.DataId} / {node.Locations.IndexOf(x)} || {minimumAngle}, {maximumAngle}");
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

    internal sealed record GatheringLocationContext(FileInfo File, ushort Id, GatheringRoot Root);
}
