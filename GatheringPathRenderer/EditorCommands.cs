using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Lumina.Excel.Sheets;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
namespace GatheringPathRenderer;

internal sealed class EditorCommands : IDisposable
{
    //private readonly Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter _playerState;
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly ICommandManager _commandManager;
    private readonly Configuration _configuration;
    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly RendererPlugin _plugin;
    private readonly IPluginLog _pluginLog;
    private readonly ITargetManager _targetManager;

    public EditorCommands(RendererPlugin plugin, IDataManager dataManager, ICommandManager commandManager,
        ITargetManager targetManager, IClientState clientState, IObjectTable objectTable,
        IChatGui chatGui, IPluginLog pluginLog, Configuration configuration)
    {
        _plugin = plugin;
        _dataManager = dataManager;
        _commandManager = commandManager;
        _targetManager = targetManager;
        _clientState = clientState;
        _objectTable = objectTable;
        //_playerState = objectTable[0]!;
        _chatGui = chatGui;
        _pluginLog = pluginLog;
        _configuration = configuration;

        _commandManager.AddHandler("/qg", new(ProcessCommand));
    }

    public void Dispose() => _commandManager.RemoveHandler("/qg");

    private void ProcessCommand(string command, string argument)
    {
        string[] parts = argument.Split(' ');
        string subCommand = parts[0];
        List<string> arguments = [.. parts.Skip(1)];

        try
        {
            switch (subCommand)
            {
                case "add":
                    CreateOrAddLocationToGroup(arguments);
                    break;
                case "clobber":
                    //AddAllMissing();
                    break;
            }
        }
        catch (Exception e)
        {
            _chatGui.PrintError(e.ToString(), "qG");
        }
    }

    private unsafe void AddAllMissing()
    {
        LayoutManager* layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null || !layout->InstancesByType.TryGetValue(InstanceType.Gathering, out Pointer<StdMap<ulong, Pointer<ILayoutInstance>>> mapPtr, false))
            return;

        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            Transform* transform = instance->GetTransformImpl();
            Vector3 position = transform->Translation;
        }
    }

    private void CreateOrAddLocationToGroup(List<string> arguments)
    {
        IGameObject? target = _targetManager.Target;
        if (target == null || target.ObjectKind != ObjectKind.GatheringPoint)
            throw new("No valid target");

        GatheringPoint gatheringPoint = _dataManager.GetExcelSheet<GatheringPoint>().GetRowOrDefault(target.BaseId) ?? throw new("Invalid gathering point");
        FileInfo targetFile;
        GatheringRoot root;
        List<RendererPlugin.GatheringLocationContext> locationsInTerritory = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).ToList();
        RendererPlugin.GatheringLocationContext? location = locationsInTerritory.SingleOrDefault(x => x.Id == gatheringPoint.GatheringPointBase.RowId);
        if (location != null)
        {
            targetFile = location.File;
            root = location.Root;

            // if this is an existing node, ignore it
            bool existingNode = root.Groups.SelectMany(x => x.Nodes.Where(y => y.DataId == target.BaseId))
                .Any(x => x.Locations.Any(y => Vector3.Distance(y.Position, target.Position) < 0.1f));
            if (existingNode)
                throw new("Node already exists");

            if (arguments.Contains("group"))
                AddToNewGroup(root, target);
            else
                AddToExistingGroup(root, target);
        }
        else
        {
            (targetFile, root) = CreateNewFile(gatheringPoint, target);
            _chatGui.Print($"Creating new file under {targetFile.FullName}", "qG");
        }

        _plugin.Save(targetFile, root);
    }

    public void AddToNewGroup(GatheringRoot root, IGameObject target)
    {
        root.Groups.Add(new()
        {
            Nodes =
            [
                new()
                {
                    DataId = target.BaseId,
                    Locations =
                    [
                        new()
                        {
                            Position = target.Position
                        }
                    ]
                }
            ]
        });
        _chatGui.Print("Added group.", "qG");
    }

    public void AddToExistingGroup(GatheringRoot root, IGameObject target)
    {
        // find the same data id
        GatheringNode? node = root.Groups.SelectMany(x => x.Nodes)
            .SingleOrDefault(x => x.DataId == target.BaseId);
        if (node != null)
        {
            node.Locations.Add(new()
            {
                Position = target.Position
            });
            _chatGui.Print($"Added location to existing node {target.BaseId}.", "qG");
        }
        else
        {
            // find the closest group
            var closestGroup = root.Groups
                .Select(group => new
                {
                    Group = group,
                    Distance = group.Nodes.Min(x =>
                        x.Locations.Min(y =>
                            Vector3.Distance(_objectTable[0]!.Position, y.Position)))
                })
                .OrderBy(x => x.Distance)
                .First();

            closestGroup.Group.Nodes.Add(new()
            {
                DataId = target.BaseId,
                Locations =
                [
                    new()
                    {
                        Position = target.Position
                    }
                ]
            });
            _chatGui.Print($"Added new node {target.BaseId}.", "qG");
        }
    }

    public unsafe (FileInfo targetFile, GatheringRoot root) CreateNewFile(GatheringPoint gatheringPoint, IGameObject target)
    {
        // determine target folder
        DirectoryInfo? targetFolder = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).FirstOrDefault()
            ?.File.Directory;
        if (targetFolder == null)
        {
            TerritoryType territoryInfo = _dataManager.GetExcelSheet<TerritoryType>().GetRow(_clientState.TerritoryType);
            targetFolder = _plugin.PathsDirectory
                .CreateSubdirectory(ExpansionData.ExpansionFolders[(EExpansionVersion)territoryInfo.ExVersion.RowId])
                .CreateSubdirectory(territoryInfo.PlaceName.Value.Name.ToString());
        }

        FileInfo targetFile =
            new(
                Path.Combine(targetFolder.FullName,
                    $"{gatheringPoint.GatheringPointBase.RowId}_{gatheringPoint.PlaceName.Value.Name}_{(PlayerState.Instance()->CurrentClassJobId == 16 ? "MIN" : "BTN")}.json"));
        GatheringRoot root = new()
        {
            Author = [_configuration.AuthorName],
            Steps =
            [
                new()
                {
                    TerritoryId = _clientState.TerritoryType,
                    InteractionType = EInteractionType.None
                }
            ],
            Groups =
            [
                new()
                {
                    Nodes =
                    [
                        new()
                        {
                            DataId = target.BaseId,
                            Locations =
                            [
                                new()
                                {
                                    Position = target.Position
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        return (targetFile, root);
    }
}
