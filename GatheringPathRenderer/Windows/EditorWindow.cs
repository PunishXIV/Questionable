using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model.Gathering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace GatheringPathRenderer.Windows;

internal sealed class EditorWindow : Window
{
    private readonly RendererPlugin _plugin;
    private readonly EditorCommands _editorCommands;
    private readonly IDataManager _dataManager;
    private readonly ITargetManager _targetManager;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;

    private readonly Dictionary<Guid, LocationOverride> _changes = [];

    private IGameObject? _target;

    private (RendererPlugin.GatheringLocationContext Context, GatheringNode Node, GatheringLocation Location)?
        _targetLocation;

    public EditorWindow(RendererPlugin plugin, EditorCommands editorCommands, IDataManager dataManager,
        ITargetManager targetManager, IClientState clientState, IObjectTable objectTable, ConfigWindow configWindow)
        : base($"Gathering Path Editor {typeof(EditorWindow).Assembly.GetName().Version!.ToString(2)}###QuestionableGatheringPathEditor",
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
        _editorCommands = editorCommands;
        _dataManager = dataManager;
        _targetManager = targetManager;
        _clientState = clientState;
        _objectTable = objectTable;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 100),
        };

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1.5f, 1),
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

    public override void Update()
    {
        if (!_clientState.IsLoggedIn || _clientState.LocalPlayer == null)
        {
            _target = null;
            _targetLocation = null;
            return;
        }

        _target = _targetManager.Target;
        var gatheringLocations = _plugin.GetLocationsInTerritory(_clientState.TerritoryType);
        var location = gatheringLocations.ToList().SelectMany(context =>
                context.Root.Groups.SelectMany(group =>
                    group.Nodes.SelectMany(node => node.Locations
                        .Select(location =>
                        {
                            float distance;
                            if (_target != null)
                                distance = Vector3.Distance(location.Position, _target.Position);
                            else
                                distance = Vector3.Distance(location.Position, _clientState.LocalPlayer.Position);

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
            .Where(x => x.ObjectKind == ObjectKind.GatheringPoint && x.DataId == location.Node.DataId)
            .Select(x => new
            {
                Object = x,
                Distance = Vector3.Distance(location.Location.Position, _clientState.LocalPlayer.Position)
            })
            .Where(x => x.Distance < 3f)
            .OrderBy(x => x.Distance)
            .Select(x => x.Object)
            .FirstOrDefault();
        _targetLocation = (location.Context, location.Node, location.Location);
    }

    public override bool DrawConditions()
    {
        return !(_clientState.TerritoryType is 0 or 939) &&
            (_target != null || _targetLocation != null);
    }

    public override void Draw()
    {
        if (_target != null && _targetLocation != null)
        {
            var context = _targetLocation.Value.Context;
            var node = _targetLocation.Value.Node;
            var location = _targetLocation.Value.Location;
            ImGui.Text(context.File.Directory?.Name ?? string.Empty);
            ImGui.Indent();
            ImGui.Text(context.File.Name);
            ImGui.Unindent();
            ImGui.Text(
                $"{_target.DataId} +{node.Locations.Count - 1} / {location.InternalId.ToString().Substring(0, 4)}");
            ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"{location.Position:G}"));

            if (!_changes.TryGetValue(location.InternalId, out LocationOverride? locationOverride))
            {
                locationOverride = new LocationOverride();
                _changes[location.InternalId] = locationOverride;
            }

            int minAngle = locationOverride.MinimumAngle ?? location.MinimumAngle.GetValueOrDefault();
            int maxAngle = locationOverride.MaximumAngle ?? location.MaximumAngle.GetValueOrDefault();
            if (ImGui.DragIntRange2("Angle", ref minAngle, ref maxAngle, 5, -360, 360))
            {
                locationOverride.MinimumAngle = minAngle;
                locationOverride.MaximumAngle = maxAngle;
            }

            float minDistance = locationOverride.MinimumDistance ?? location.CalculateMinimumDistance();
            float maxDistance = locationOverride.MaximumDistance ?? location.CalculateMaximumDistance();
            if (ImGui.DragFloatRange2("Distance", ref minDistance, ref maxDistance, 0.1f, 1f, 3f))
            {
                locationOverride.MinimumDistance = minDistance;
                locationOverride.MaximumDistance = maxDistance;
            }

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
            {
                _changes[location.InternalId] = new LocationOverride();
            }

            ImGui.EndDisabled();


            List<IGameObject> nodesInObjectTable = _objectTable
                .Where(x => x.ObjectKind == ObjectKind.GatheringPoint && x.DataId == _target.DataId)
                .ToList();
            List<IGameObject> missingLocations = nodesInObjectTable
                .Where(x => !node.Locations.Any(y => Vector3.Distance(x.Position, y.Position) < 0.1f))
                .ToList();
            if (missingLocations.Count > 0)
            {
                if (ImGui.Button("Add missing locations"))
                {
                    foreach (var missing in missingLocations)
                        _editorCommands.AddToExistingGroup(context.Root, missing);

                    _plugin.Save(context.File, context.Root);
                }
            }
        }
        else if (_target != null)
        {
            var gatheringPoint = _dataManager.GetExcelSheet<GatheringPoint>().GetRowOrDefault(_target.DataId);
            if (gatheringPoint == null)
                return;

            var locationsInTerritory = _plugin.GetLocationsInTerritory(_clientState.TerritoryType).ToList();
            var location = locationsInTerritory.SingleOrDefault(x => x.Id == gatheringPoint.Value.GatheringPointBase.RowId);
            if (location != null)
            {
                var targetFile = location.File;
                var root = location.Root;

                if (ImGui.Button("Add to closest group"))
                {
                    _editorCommands.AddToExistingGroup(root, _target);
                    _plugin.Save(targetFile, root);
                }

                ImGui.BeginDisabled(root.Groups.Any(group => group.Nodes.Any(node => node.DataId == _target.DataId)));
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
                    var (targetFile, root) = _editorCommands.CreateNewFile(gatheringPoint.Value, _target);
                    _plugin.Save(targetFile, root);
                }
            }
        }
    }

    public bool TryGetOverride(Guid internalId, out LocationOverride? locationOverride)
        => _changes.TryGetValue(internalId, out locationOverride);
}

internal sealed class LocationOverride
{
    public int? MinimumAngle { get; set; }
    public int? MaximumAngle { get; set; }
    public float? MinimumDistance { get; set; }
    public float? MaximumDistance { get; set; }

    public bool IsCone()
    {
        return MinimumAngle != null && MaximumAngle != null && MinimumAngle != MaximumAngle;
    }

    public bool NeedsSave()
    {
        return (MinimumAngle != null && MaximumAngle != null) || (MinimumDistance != null && MaximumDistance != null);
    }
}
