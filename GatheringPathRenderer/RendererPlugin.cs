using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatheringPathRenderer.Windows;
using Pictomancy;
using Questionable.Model.Gathering;

namespace GatheringPathRenderer;

public sealed class RendererPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(nameof(RendererPlugin));
    private readonly List<uint> _colors = [0x40FF2020, 0x4020FF20, 0x402020FF, 0x40FFFF20, 0x40FF20FF, 0x4020FFFF];

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    //private readonly IPlayerState _playerState;
    private readonly IPluginLog _pluginLog;

    private readonly EditorCommands _editorCommands;
    private readonly EditorWindow _editorWindow;

    private readonly List<GatheringLocationContext> _gatheringLocations = [];
    private readonly Dictionary<uint, List<Vector3>> _gbrLocationData;
    private Job _currentClassJob = Job.ADV;

    internal List<GatheringLocationContext> GatheringLocations => _gatheringLocations;
    internal Dictionary<uint, List<Vector3>> GBRLocationData => _gbrLocationData;
    internal bool DistantRange { get; set; }

    public RendererPlugin(IDalamudPluginInterface pluginInterface, IClientState clientState,
        ICommandManager commandManager, IDataManager dataManager, ITargetManager targetManager, IChatGui chatGui,
        IObjectTable objectTable, IPluginLog pluginLog, IFramework framework)
    {
        _pluginInterface = pluginInterface;
        _clientState = clientState;
        _objectTable = objectTable;
        //_playerState = playerState;
        _pluginLog = pluginLog;
        _gbrLocationData = LoadGBRPosData(_pluginInterface.AssemblyLocation.DirectoryName!);
        pluginLog.Info($"Loaded {_gbrLocationData.Count} entries from GBR data");
        ECommonsMain.Init(pluginInterface, this);

        Configuration? configuration = (Configuration?)pluginInterface.GetPluginConfig();
        if (configuration == null)
        {
            configuration = new Configuration();
            pluginInterface.SavePluginConfig(configuration);
        }

        _editorCommands = new EditorCommands(this, dataManager, commandManager, targetManager, clientState,
            objectTable, chatGui, pluginLog, configuration);
        var configWindow = new ConfigWindow(pluginInterface, configuration);
        _editorWindow = new EditorWindow(this, _editorCommands, dataManager, commandManager, targetManager, clientState, objectTable,
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

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw += Draw;
        _clientState.ClassJobChanged += ClassJobChanged;

        //commandManager.AddHandler("/qipc", new CommandInfo(CallIPC));
    }

    //private void CallIPC(string command, string argument)
    //{
    //    string[] parts = argument.Split(' ');
    //    string function = parts[0];
    //    List<Type> types = [];
    //    List<object> arguments = [];
    //    char delim = ':';
    //    foreach (string part in parts.Skip(1).ToArray())
    //    {
    //        char t;
    //        string v;
    //        if (!part.Contains(':'))
    //        {
    //            t = 's';
    //            v = part;
    //        }
    //        else
    //        {
    //            var _ = part.Split(':', 2);
    //            t = char.Parse(_[0]);
    //            v = _[1];
    //        }
    //        switch (t)
    //        {
    //            case 'i':
    //                types.Add(typeof(int));
    //                if (v.Length != 0)
    //                    arguments.Add(int.Parse(v));
    //                break;
    //            case 'b':
    //                types.Add(typeof(bool));
    //                if (v.Length != 0)
    //                    arguments.Add(bool.Parse(v));
    //                break;
    //            case 'u':
    //                types.Add(typeof(uint));
    //                if (v.Length != 0)
    //                    arguments.Add(uint.Parse(v));
    //                break;
    //            case 'h':
    //                types.Add(typeof(ushort));
    //                if (v.Length != 0)
    //                    arguments.Add(ushort.Parse(v));
    //                break;
    //            case 'y':
    //                types.Add(typeof(byte));
    //                if (v.Length != 0)
    //                    arguments.Add(byte.Parse(v));
    //                break;
    //            default:
    //                types.Add(typeof(string));
    //                if (v.Length != 0)
    //                    arguments.Add((string)v);
    //                break;
    //        }
    //    }
    //    var _types = types.ToArray();
    //    var _arguments = arguments.ToArray();
    //    _pluginLog.Debug(_types.Print(","));
    //    _pluginLog.Debug(_arguments.Print(","));
    //    _pluginLog.Debug($"{_types.Length},{_arguments.Length}");
    //    _pluginLog.Debug($"Attempting to call {function}({_arguments.Print()})");
    //    EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(_pluginInterface, function.Split('.')[0], SafeWrapper.IPCException);

    //    MethodInfo? method1 = typeof(IDalamudPluginInterface).GetMethod("GetIpcSubscriber", _types.Length, _types);
    //    MethodInfo? func1 = method1?.MakeGenericMethod(_types);
    //    object? callGateSubscriber = func1?.Invoke(_pluginInterface, [function]);
    //    MethodInfo? method2 = typeof(ICallGateSubscriber).GetMethod("InvokeFunc", _types.Length, _types);
    //    MethodInfo? func2 = method2?.MakeGenericMethod(_types);
    //    func2?.Invoke(callGateSubscriber, _arguments);
    //    //ICallGateSubscriber<string,bool> callGateSubscriber = _pluginInterface.GetIpcSubscriber<string,bool>(function);
    //    //_chatGui.Print(callGateSubscriber.InvokeFunc(args[0]).ToString(), "qipc");
    //    foreach (var token in _disposalTokens) token.Dispose();
    //    //else if (parts.Length == 2)
    //    //{
    //    //    MethodInfo method0 = typeof(arguments[0].GetType());
    //    //    ICallGateProvider<type,bool> callGateSubscriber = _pluginInterface.GetIpcSubscriber<T,bool>(function);
    //    //}

    //}

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

    internal void Reload() => LoadGatheringLocationsFromDirectory();

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
        var locationNode = JsonNode.Parse(stream)!;
        GatheringRoot root = locationNode.Deserialize<GatheringRoot>()!;
        _gatheringLocations.Add(new GatheringLocationContext(fileInfo, ushort.Parse(fileInfo.Name.Split('_')[0]),
            root));
    }

    public static Dictionary<uint, List<Vector3>> LoadGBRPosData(string directoryName)
    {
        var path = Path.Combine(directoryName, "world_locations.json");
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
        var root = JsonNode.Parse(stream);
        var result = new Dictionary<uint, List<Vector3>>();

        if (root is not JsonObject obj)
            return result;

        foreach (var kvp in obj)
        {
            if (!uint.TryParse(kvp.Key, out uint key))
                continue;

            var vectorList = new List<Vector3>();
            if (kvp.Value is JsonArray arr)
            {
                foreach (var vecNode in arr)
                {
                    float x = vecNode?["X"]?.GetValue<float>() ?? 0f;
                    float y = vecNode?["Y"]?.GetValue<float>() ?? 0f;
                    float z = vecNode?["Z"]?.GetValue<float>() ?? 0f;
                    vectorList.Add(new Vector3(x, y, z));
                }
            }

            result[key] = vectorList;
        }

        return result;
    }

    internal IEnumerable<GatheringLocationContext> GetLocationsInTerritory(uint territoryId)
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
            JsonObject newNode = new()
            {
                {
                    "$schema",
                    "https://qstxiv.github.io/schema/gatheringlocation-v1.json"
                }
            };
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
                property.ShouldSerialize = (_, val) => val is ICollection { Count: > 0 };
        }
    }

    private void ClassJobChanged(uint classJobId) => _currentClassJob = (Job)classJobId;

    private void Draw()
    {
        if (!ExcelJobHelper.IsDol(_currentClassJob))
            return;

        using var drawList = PctService.Draw();
        if (drawList == null)
            return;

        Vector3 position = _objectTable[0]?.Position ?? Vector3.Zero;
        float drawDistance = DistantRange ? 20000f : 200f;
        foreach (var location in GetLocationsInTerritory(_clientState.TerritoryType))
        {
            if (!location.Root.Groups.Any(gr =>
                    gr.Nodes.Any(
                        no => no.Locations.Any(
                            loc => Vector3.Distance(loc.Position, position) < drawDistance))))
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

        PctService.Dispose();

        _pluginInterface.GetIpcSubscriber<object>("Questionable.ReloadData")
            .Unsubscribe(Reload);

        _editorCommands.Dispose();
    }

    internal sealed record GatheringLocationContext(FileInfo File, ushort Id, GatheringRoot Root);
}
