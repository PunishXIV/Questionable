using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;

namespace GatheringPathRenderer;

internal sealed class EditorCommands : IDisposable
{
    private readonly RendererPlugin _plugin;
    private readonly IDataManager _dataManager;
    private readonly ICommandManager _commandManager;
    private readonly ITargetManager _targetManager;
    private readonly IClientState _clientState;
    private readonly IChatGui _chatGui;
    private readonly Configuration _configuration;

    public EditorCommands(RendererPlugin plugin, IDataManager dataManager, ICommandManager commandManager,
        ITargetManager targetManager, IClientState clientState, IChatGui chatGui, Configuration configuration)
    {
        _plugin = plugin;
        _dataManager = dataManager;
        _commandManager = commandManager;
        _targetManager = targetManager;
        _clientState = clientState;
        _chatGui = chatGui;
        _configuration = configuration;

        _commandManager.AddHandler("/qg", new CommandInfo(ProcessCommand));
    }

    private void ProcessCommand(string command, string argument)
    {
        string[] parts = argument.Split(' ');
        string subCommand = parts[0];
        List<string> arguments = parts.Skip(1).ToList();

        try
        {
            switch (subCommand)
            {
                case "add":
                    CreateOrAddLocationToGroup(arguments);
                    break;
            }
        }
        catch (Exception e)
        {
            _chatGui.PrintError(e.ToString(), "qG");
        }
    }

    private void CreateOrAddLocationToGroup(List<string> arguments)
    {
        var target = _targetManager.Target;
        if (target == null || target.ObjectKind != ObjectKind.GatheringPoint)
            throw new Exception("No valid target");

        var gatheringPoint = _dataManager.GetExcelSheet<GatheringPoint>().GetRowOrDefault(target.DataId);
        if (gatheringPoint == null)
            throw new Exception("Invalid gathering point");

        FileInfo targetFile;
        GatheringRoot root;
        var locationsInTerritory = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).ToList();
        var location = locationsInTerritory.SingleOrDefault(x => x.Id == gatheringPoint.Value.GatheringPointBase.RowId);
        if (location != null)
        {
            targetFile = location.File;
            root = location.Root;

            // if this is an existing node, ignore it
            var existingNode = root.Groups.SelectMany(x => x.Nodes.Where(y => y.DataId == target.DataId))
                .Any(x => x.Locations.Any(y => Vector3.Distance(y.Position, target.Position) < 0.1f));
            if (existingNode)
                throw new Exception("Node already exists");

            if (arguments.Contains("group"))
                AddToNewGroup(root, target);
            else
                AddToExistingGroup(root, target);
        }
        else
        {
            (targetFile, root) = CreateNewFile(gatheringPoint.Value, target);
            _chatGui.Print($"Creating new file under {targetFile.FullName}", "qG");
        }

        _plugin.Save(targetFile, root);
    }

    public void AddToNewGroup(GatheringRoot root, IGameObject target)
    {
        root.Groups.Add(new GatheringNodeGroup
        {
            Nodes =
            [
                new GatheringNode
                {
                    DataId = target.DataId,
                    Locations =
                    [
                        new GatheringLocation
                        {
                            Position = target.Position,
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
        var node = root.Groups.SelectMany(x => x.Nodes)
            .SingleOrDefault(x => x.DataId == target.DataId);
        if (node != null)
        {
            node.Locations.Add(new GatheringLocation
            {
                Position = target.Position,
            });
            _chatGui.Print($"Added location to existing node {target.DataId}.", "qG");
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
                            Vector3.Distance(_clientState.LocalPlayer!.Position, y.Position)))
                })
                .OrderBy(x => x.Distance)
                .First();

            closestGroup.Group.Nodes.Add(new GatheringNode
            {
                DataId = target.DataId,
                Locations =
                [
                    new GatheringLocation
                    {
                        Position = target.Position,
                    }
                ]
            });
            _chatGui.Print($"Added new node {target.DataId}.", "qG");
        }
    }

    public (FileInfo targetFile, GatheringRoot root) CreateNewFile(GatheringPoint gatheringPoint, IGameObject target)
    {
        // determine target folder
        DirectoryInfo? targetFolder = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).FirstOrDefault()
            ?.File.Directory;
        if (targetFolder == null)
        {
            var territoryInfo = _dataManager.GetExcelSheet<TerritoryType>().GetRow(_clientState.TerritoryType);
            targetFolder = _plugin.PathsDirectory
                .CreateSubdirectory(ExpansionData.ExpansionFolders[(EExpansionVersion)territoryInfo.ExVersion.RowId])
                .CreateSubdirectory(territoryInfo.PlaceName.Value.Name.ToString());
        }

        FileInfo targetFile =
            new FileInfo(
                Path.Combine(targetFolder.FullName,
                    $"{gatheringPoint.GatheringPointBase.RowId}_{gatheringPoint.PlaceName.Value.Name}_{(_clientState.LocalPlayer!.ClassJob.RowId == 16 ? "MIN" : "BTN")}.json"));
        var root = new GatheringRoot
        {
            Author = [_configuration.AuthorName],
            Steps =
            [
                new QuestStep
                {
                    TerritoryId = _clientState.TerritoryType,
                    InteractionType = EInteractionType.None,
                }
            ],
            Groups =
            [
                new GatheringNodeGroup
                {
                    Nodes =
                    [
                        new GatheringNode
                        {
                            DataId = target.DataId,
                            Locations =
                            [
                                new GatheringLocation
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

    public void Dispose()
    {
        _commandManager.RemoveHandler("/qg");
    }
}
