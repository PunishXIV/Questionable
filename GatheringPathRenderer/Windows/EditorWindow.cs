using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Model.Gathering;
namespace GatheringPathRenderer.Windows;

internal sealed class EditorWindow : Window
{

    private readonly Dictionary<Guid, LocationOverride> _changes = [];
    private readonly IClientState _clientState;
    private readonly ICommandManager _commandManager;
    private readonly IDataManager _dataManager;
    private readonly EditorCommands _editorCommands;
    private readonly IObjectTable _objectTable;
    private readonly RendererPlugin _plugin;
    private readonly ITargetManager _targetManager;

    private IGameObject? _target;

    private (RendererPlugin.GatheringLocationContext Context, GatheringNode Node, GatheringLocation Location)?
        _targetLocation;
    private bool compact;
    private int count;
    private FilterClass filterClass = FilterClass.None;
    private bool showAll;
    private bool sortByDistance = true;
    private bool unaddedVisible = true;

    public EditorWindow(RendererPlugin plugin, EditorCommands editorCommands, IDataManager dataManager, ICommandManager commandManager,
        ITargetManager targetManager, IClientState clientState, IObjectTable objectTable, ConfigWindow configWindow)
        : base($"Gathering Path Editor {typeof(EditorWindow).Assembly.GetName().Version!.ToString(2)}###QuestionableGatheringPathEditor",
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.AlwaysVerticalScrollbar)
    {
        _plugin = plugin;
        _editorCommands = editorCommands;
        _dataManager = dataManager;
        _commandManager = commandManager;
        _targetManager = targetManager;
        _clientState = clientState;
        _objectTable = objectTable;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(-1, 100)
        };

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(1.5f, 1),
            Click = _ => configWindow.IsOpen = true,
            Priority = int.MinValue,
            ShowTooltip = () =>
            {
                ImGui.BeginTooltip();
                ImGui.Text("Open Configuration");
                ImGui.EndTooltip();
            }
        });

        RespectCloseHotkey = false;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
    }

    public override unsafe void Update()
    {
        if (!_clientState.IsLoggedIn || _objectTable[0] == null)
        {
            _target = null;
            _targetLocation = null;
            unaddedVisible = true;
            return;
        }

        if (_objectTable[0] != null && !PlayerState.Instance()->CurrentClassJobId.InRange(16, 18))
            unaddedVisible = true;

        _target = _targetManager.Target;
        IEnumerable<RendererPlugin.GatheringLocationContext> gatheringLocations = _plugin.GetLocationsInTerritory(_clientState.TerritoryType);
        var location = gatheringLocations.ToList().SelectMany(context =>
                context.Root.Groups.SelectMany(group =>
                    group.Nodes.SelectMany(node => node.Locations
                        .Select(location =>
                        {
                            float distance;
                            if (_target != null)
                                distance = Vector3.Distance(location.Position, _target.Position);
                            else
                                distance = Vector3.Distance(location.Position, _objectTable[0]!.Position);

                            return new { Context = context, Node = node, Location = location, Distance = distance };
                        })
                        .Where(location => location.Distance < (_target == null ? 3f : 0.1f)))))
            .MinBy(x => x.Distance);
        if (_target != null && _target.ObjectKind != ObjectKind.GatheringPoint)
        {
            _target = null;
            _targetLocation = null;
            return;
        }

        if (location == null)
        {
            _targetLocation = null;
            return;
        }

        _target ??= _objectTable
            .Where(x => x.ObjectKind == ObjectKind.GatheringPoint && x.BaseId == location.Node.DataId)
            .Select(x => new
            {
                Object = x,
                Distance = Vector3.Distance(location.Location.Position, _objectTable[0]!.Position)
            })
            .Where(x => x.Distance < 3f)
            .OrderBy(x => x.Distance)
            .Select(x => x.Object)
            .FirstOrDefault();
        _targetLocation = (location.Context, location.Node, location.Location);
    }

    public override unsafe bool DrawConditions() => (_objectTable[0] != null && PlayerState.Instance()->CurrentClassJobId.InRange(16, 18)) &&
                                                    _clientState.TerritoryType is not 0 && (_target != null || _targetLocation != null || unaddedVisible);

    public override void Draw()
    {
        if (_target != null && _targetLocation != null)
        {
            RendererPlugin.GatheringLocationContext context = _targetLocation.Value.Context;
            GatheringNode node = _targetLocation.Value.Node;
            GatheringLocation location = _targetLocation.Value.Location;
            ImGui.Text(context.File.Directory?.Name ?? string.Empty);
            ImGui.Indent();
            ImGui.Text(context.File.Name);
            ImGui.Unindent();
            ImGui.Text(
                $"{_target.BaseId} +{node.Locations.Count - 1} / {location.InternalId.ToString()[..4]}");
            ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"{location.Position:G}"));

            if (!_changes.TryGetValue(location.InternalId, out LocationOverride? locationOverride))
            {
                locationOverride = new();
                _changes[location.InternalId] = locationOverride;
            }

            int minAngle = locationOverride.MinimumAngle ?? location.MinimumAngle.GetValueOrDefault();
            int maxAngle = locationOverride.MaximumAngle ?? location.MaximumAngle.GetValueOrDefault();
            if (ImGui.DragIntRange2("Angle", ref minAngle, ref maxAngle, 5, -360, 360))
            {
                locationOverride.MinimumAngle = minAngle;
                if (minAngle >= maxAngle) maxAngle = 360;
                locationOverride.MaximumAngle = maxAngle;
            }

            float minDistance = locationOverride.MinimumDistance ?? location.CalculateMinimumDistance();
            float maxDistance = locationOverride.MaximumDistance ?? location.CalculateMaximumDistance();
            if (ImGui.DragFloatRange2("Distance", ref minDistance, ref maxDistance, 0.1f, 1f, 3f))
            {
                locationOverride.MinimumDistance = minDistance;
                locationOverride.MaximumDistance = maxDistance;
            }

            if (ImGui.Button("60deg"))
            {
                locationOverride.MinimumAngle = minAngle <= 270 ? minAngle : minAngle - 360;
                locationOverride.MaximumAngle = maxAngle = minAngle <= 270 ? minAngle + 60 : minAngle - 360 + 60;
            }
            ImGui.SameLine();

            bool unsaved = locationOverride.NeedsSave();
            ImGui.BeginDisabled(!unsaved);
            if (unsaved)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
            if (ImGui.Button("Save"))
            {
                if (locationOverride is { MinimumAngle: not null, MaximumAngle: not null })
                {
                    location.MinimumAngle = locationOverride.MinimumAngle ?? location.MinimumAngle;
                    location.MaximumAngle = locationOverride.MaximumAngle ?? location.MaximumAngle;
                }

                if (locationOverride is { MinimumDistance: not null, MaximumDistance: not null })
                {
                    location.MinimumDistance = locationOverride.MinimumDistance;
                    location.MaximumDistance = locationOverride.MaximumDistance;
                }

                _plugin.Save(context.File, context.Root);
            }

            if (unsaved)
                ImGui.PopStyleColor();

            ImGui.SameLine();
            if (ImGui.Button("Reset"))
                _changes[location.InternalId] = new();

            ImGui.EndDisabled();


            List<IGameObject> nodesInObjectTable = [.. _objectTable.Where(x => x.ObjectKind == ObjectKind.GatheringPoint && x.BaseId == _target.BaseId)];
            List<IGameObject> missingLocations = [.. nodesInObjectTable.Where(x => !node.Locations.Any(y => Vector3.Distance(x.Position, y.Position) < 0.1f))];
            if (missingLocations.Count > 0)
            {
                if (ImGui.Button("Add missing locations"))
                {
                    foreach (IGameObject missing in missingLocations)
                        _editorCommands.AddToExistingGroup(context.Root, missing);

                    _plugin.Save(context.File, context.Root);
                }
            }
        }
        else if (_target != null)
        {
            GatheringPoint? gatheringPoint = _dataManager.GetExcelSheet<GatheringPoint>().GetRowOrDefault(_target.BaseId);
            if (gatheringPoint == null)
                return;

            List<RendererPlugin.GatheringLocationContext> locationsInTerritory = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).ToList();
            RendererPlugin.GatheringLocationContext? location = locationsInTerritory.SingleOrDefault(x => x.Id == gatheringPoint.Value.GatheringPointBase.RowId);
            if (location != null)
            {
                FileInfo targetFile = location.File;
                GatheringRoot root = location.Root;

                if (ImGui.Button("Add to closest group"))
                {
                    _editorCommands.AddToExistingGroup(root, _target);
                    _plugin.Save(targetFile, root);
                }

                ImGui.BeginDisabled(root.Groups.Any(group => group.Nodes.Any(node => node.DataId == _target.BaseId)));
                ImGui.SameLine();
                if (ImGui.Button("Add as new group"))
                {
                    _editorCommands.AddToNewGroup(root, _target);
                    _plugin.Save(targetFile, root);
                }

                ImGui.EndDisabled();
            }
            else
            {
                if (ImGui.Button($"Create location ({gatheringPoint.Value.GatheringPointBase.RowId})"))
                {
                    (FileInfo targetFile, GatheringRoot root) = _editorCommands.CreateNewFile(gatheringPoint.Value, _target);
                    _plugin.Save(targetFile, root);
                }
            }
        }
        if (_clientState.TerritoryType != 0 && _objectTable[0] != null && ImGui.CollapsingHeader("Unadded nodes"))
            ListLocationsInCurrentTerritory();
    }

    public bool TryGetOverride(Guid internalId, out LocationOverride? locationOverride)
        => _changes.TryGetValue(internalId, out locationOverride);

    public void ListLocationsInCurrentTerritory()
    {
        uint a = _clientState.TerritoryType;
        IEnumerable<GatheringPoint> gatheringPoints = _dataManager.GetExcelSheet<GatheringPoint>().Where(
            _point => _point.TerritoryType.RowId.Equals(_clientState.TerritoryType) &&
                      _point.GatheringPointBase.Value.GatheringType.RowId <= (uint)GatheringType.Harvesting
        );
        List<RendererPlugin.GatheringLocationContext> loadedPoints = _plugin.GatheringLocations;
        bool shownNone = true;
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
            _commandManager.ProcessCommand("/vnav stop");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("vnav stop");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(compact ? FontAwesomeIcon.Expand : FontAwesomeIcon.Compress))
            compact = !compact;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("compact");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(_plugin.DistantRange ? FontAwesomeIcon.Binoculars : FontAwesomeIcon.Eye))
            _plugin.DistantRange = !_plugin.DistantRange;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("distant");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(sortByDistance ? FontAwesomeIcon.SortNumericUp : FontAwesomeIcon.SortAlphaDown))
            sortByDistance = !sortByDistance;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("sort by distance/class");

        ImGui.SameLine();
        FontAwesomeIcon filterClassIcon = FontAwesomeIcon.Notdef;
        if (filterClass.Equals(FilterClass.Miner)) filterClassIcon = FontAwesomeIcon.HandRock;
        if (filterClass.Equals(FilterClass.Botanist)) filterClassIcon = FontAwesomeIcon.HandPaper;
        if (ImGuiComponents.IconButton(filterClassIcon))
            filterClass = (FilterClass)(((int)filterClass + 1) % Enum.GetValues<FilterClass>().Length);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("filter none/min/btn");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(showAll ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash))
            showAll = !showAll;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("show nodes inc added");

        ImGui.Text($"Nodes in {_clientState.TerritoryType}: ({count})");
        List<string> seen = [];
        count = 0;
        Dictionary<uint, Tuple<string, string, bool, float, bool>> output = [];
        foreach (GatheringPoint _point in gatheringPoints.OrderBy(x => x.PlaceName.Value.Name.ToMacroString()))
        {
            if (_point.GatheringPointBase.RowId >= 653 && _point.GatheringPointBase.RowId <= 680) continue; // obsolete skybuilders stuff
            bool alreadyAdded = false;
            if (loadedPoints.Any(location => location.Root.Groups.Any(group => group.Nodes.Any(node => node.DataId.Equals(_point.RowId)))))
            {
                if (showAll)
                    alreadyAdded = true;
                else
                    continue;
            }
            count += 1;
            if (compact)
            {
                if (seen.Contains(_point.PlaceName.Value.Name.ToMacroString())) continue;
                seen.Add(_point.PlaceName.Value.Name.ToMacroString());
            }
            char special = ' ';
            if (_dataManager.GetExcelSheet<GatheringPointTransient>().TryGetRow(_point.RowId, out GatheringPointTransient gatheringPointTransient) &&
                (gatheringPointTransient.EphemeralStartTime != 65535 ||
                 gatheringPointTransient.EphemeralEndTime != 65535 ||
                 gatheringPointTransient.GatheringRarePopTimeTable.RowId != 0))
            {
                special = '*';
            }
            GatheringType gatheringType = (GatheringType)_point.GatheringPointBase.Value.GatheringType.RowId;
            if (filterClass.Equals(FilterClass.Miner) && !gatheringType.Equals(GatheringType.Mining) && !gatheringType.Equals(GatheringType.Quarrying)) continue;
            if (filterClass.Equals(FilterClass.Botanist) && !gatheringType.Equals(GatheringType.Logging) && !gatheringType.Equals(GatheringType.Harvesting)) continue;
            string line = $"{gatheringType.ToString()[..1]}{special}{_point.RowId} {_point.PlaceName.Value.Name}  ";
            string coords = "";
            bool orange = false;
            float distance = 0.0f;
            if (_plugin.GBRLocationData.TryGetValue(_point.RowId, out List<Vector3>? value))
            {
                Vector3? gbr = value.FirstOrNull();
                if (gbr != null)
                {
                    ushort scale = _point.TerritoryType.Value.Map.Value.SizeFactor;
                    coords = $"{gbr.Value.X} {gbr.Value.Y} {gbr.Value.Z}";
                    line += coords;
                    distance = (_objectTable[0]!.Position - gbr).Value.Length();
                    if (distance < 200)
                    {
                        line += $"  ({distance:F2})";
                        orange = true;
                    }
                    else if (distance < 500 && !_plugin.DistantRange || _plugin.DistantRange)
                        line += $"  ({distance:F2})";
                    else
                        continue;
                }
            }
            output.Add(_point.RowId, new(line, coords, orange, distance, alreadyAdded));
            shownNone = false;
        }
        if (!shownNone)
        {
            IOrderedEnumerable<Tuple<string, string, bool, float, bool>> sorted = sortByDistance ? output.Values.OrderBy(t => t.Item4) : output.Values.OrderBy(t => t.Item1);
            foreach ((string line, string coords, bool orange, float distance, bool alreadyAdded) in sorted)
            {
                if (alreadyAdded)
                    ImGui.TextColored(ImGuiColors.DalamudGrey2, line);
                else if (orange)
                    ImGui.TextColored(ImGuiColors.DalamudOrange, line);
                else
                    ImGui.Text(line);
                if (ImGui.IsItemClicked())
                    _commandManager.ProcessCommand($"/vnav flyto {coords}");
            }
        }
        else
        {
            ImGui.Text($"No (unadded) results. [pinned {(unaddedVisible ? 'y' : 'n')}]");
            if (ImGui.IsItemClicked()) unaddedVisible = !unaddedVisible;
        }
    }

    private enum GatheringType
    {
        Mining,
        Quarrying,
        Logging,
        Harvesting,
        Fishing,
        Spearfishing
    }

    private enum FilterClass
    {
        None,
        Miner,
        Botanist
    }
}

internal sealed class LocationOverride
{
    public int? MinimumAngle { get; set; }
    public int? MaximumAngle { get; set; }
    public float? MinimumDistance { get; set; }
    public float? MaximumDistance { get; set; }

    public bool IsCone() => MinimumAngle != null && MaximumAngle != null && MinimumAngle != MaximumAngle;

    public bool NeedsSave() => (MinimumAngle != null && MaximumAngle != null) || (MinimumDistance != null && MaximumDistance != null);
}
