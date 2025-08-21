using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Model;
using Questionable.Model.Gathering;

namespace Questionable.Windows.JournalComponents;

internal sealed class GatheringJournalComponent
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly UiUtils _uiUtils;
    private readonly GatheringPointRegistry _gatheringPointRegistry;
    private readonly Dictionary<int, string> _gatheringItems;
    private readonly List<ExpansionPoints> _gatheringPointsByExpansion;
    private readonly List<ushort> _gatheredItems = [];

    private List<FilteredExpansion> _filteredExpansions = [];
    private string _searchText = string.Empty;

    private delegate byte GetIsGatheringItemGatheredDelegate(ushort item);

    [Signature("48 89 5C 24 ?? 57 48 83 EC 20 8B D9 8B F9")]
    private GetIsGatheringItemGatheredDelegate _getIsGatheringItemGathered = null!;

    private bool IsGatheringItemGathered(uint item) => _getIsGatheringItemGathered((ushort)item) != 0;

    public GatheringJournalComponent(IDataManager dataManager, IDalamudPluginInterface pluginInterface, UiUtils uiUtils,
        IGameInteropProvider gameInteropProvider, GatheringPointRegistry gatheringPointRegistry)
    {
        _pluginInterface = pluginInterface;
        _uiUtils = uiUtils;
        _gatheringPointRegistry = gatheringPointRegistry;

        // TODO some of the logic here would be better suited elsewhere, in particular the [item] → [gathering item] → [location] lookup
        var routeToGatheringPoint = dataManager.GetExcelSheet<GatheringLeveRoute>()
            .Where(x => x.GatheringPoint[0].RowId != 0)
            .SelectMany(x => x.GatheringPoint
                .Where(y => y.RowId != 0)
                .Select(y => new
                {
                    RouteId = x.RowId,
                    GatheringPointId = y.RowId
                }))
            .GroupBy(x => x.RouteId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.GatheringPointId).ToList());
        var gatheringLeveSheet = dataManager.GetExcelSheet<GatheringLeve>();
        var territoryTypeSheet = dataManager.GetExcelSheet<TerritoryType>();
        var leveGatheringPoints = dataManager.GetExcelSheet<Leve>()
            .Where(x => x.RowId > 0)
            .Select(x => gatheringLeveSheet.GetRowOrDefault(x.DataId.RowId))
            .Where(x => x != null)
            .Cast<GatheringLeve>()
            .SelectMany(x => x.Route)
            .Where(y => y.RowId != 0)
            .SelectMany(y => routeToGatheringPoint[y.RowId])
            .Distinct()
            .ToHashSet();

        var itemSheet = dataManager.GetExcelSheet<Item>();

        _gatheringItems = dataManager.GetExcelSheet<GatheringItem>()
            .Where(x => x.RowId != 0 && x.GatheringItemLevel.RowId != 0)
            .Select(x => new
            {
                GatheringItemId = (int)x.RowId,
                Name = itemSheet.GetRowOrDefault(x.Item.RowId)?.Name.ToString()
            })
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .ToDictionary(x => x.GatheringItemId, x => x.Name!);

        _gatheringPointsByExpansion = dataManager.GetExcelSheet<GatheringPoint>()
            .Where(x => x.GatheringPointBase.RowId != 0)
            .Where(x => x.GatheringPointBase.RowId is < 653 or > 680) // exclude ishgard restoration phase 1
            .DistinctBy(x => x.GatheringPointBase.RowId)
            .Select(x => new
            {
                GatheringPointId = x.RowId,
                Point = new DefaultGatheringPoint(new GatheringPointId((ushort)x.GatheringPointBase.RowId),
                    x.GatheringPointBase.Value.GatheringType.RowId switch
                    {
                        0 or 1 => EClassJob.Miner,
                        2 or 3 => EClassJob.Botanist,
                        _ => EClassJob.Fisher
                    },
                    x.GatheringPointBase.Value.GatheringLevel,
                    x.GatheringPointBase.Value.Item.Where(y => y.RowId != 0).Select(y => (ushort)y.RowId).ToList(),
                    (EExpansionVersion?)x.TerritoryType.ValueNullable?.ExVersion.RowId ?? (EExpansionVersion)byte.MaxValue,
                    (ushort)x.TerritoryType.RowId,
                    x.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString(),
                    $"{x.GatheringPointBase.RowId} - {x.PlaceName.ValueNullable?.Name}")
            })
            .Where(x => x.Point.ClassJob != EClassJob.Fisher)
            .Select(x =>
            {
                if (leveGatheringPoints.Contains(x.GatheringPointId))
                    return null;
                else if (x.Point.TerritoryType == 1 &&
                         _gatheringPointRegistry.TryGetGatheringPoint(x.Point.Id, out GatheringRoot? gatheringRoot))
                {
                    // for some reason the game doesn't know where this gathering location is
                    var territoryType = territoryTypeSheet.GetRow(gatheringRoot.Steps.Last().TerritoryId);
                    return x.Point with
                    {
                        Expansion = (EExpansionVersion)territoryType.ExVersion.RowId,
                        TerritoryType = (ushort)territoryType.RowId,
                        TerritoryName = territoryType.PlaceName.ValueNullable?.Name.ToString(),
                    };
                }
                else
                    return x.Point;
            })
            .Where(x => x != null)
            .Cast<DefaultGatheringPoint>()
            .Where(x => x.Expansion != (EExpansionVersion)byte.MaxValue)
            .Where(x => x.GatheringItemIds.Count > 0)
            .Where(x => x.TerritoryType is not 901 and not 929) // exclude old diadem
            .GroupBy(x => x.Expansion)
            .Select(x => new ExpansionPoints(x.Key, x
                .GroupBy(y => new
                {
                    y.TerritoryType,
                    TerritoryName =
                        $"{(!string.IsNullOrEmpty(y.TerritoryName) ? y.TerritoryName : "???")} ({y.TerritoryType})"
                })
                .Select(y => new TerritoryPoints(y.Key.TerritoryType, y.Key.TerritoryName, y.ToList()))
                .Where(y => y.Points.Count > 0)
                .ToList()))
            .OrderBy(x => x.ExpansionVersion)
            .ToList();

        gameInteropProvider.InitializeFromAttributes(this);
    }

    public void DrawGatheringItems()
    {
        using var tab = ImRaii.TabItem("Gathering Points");
        if (!tab)
            return;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint(string.Empty, "Search areas, gathering points and items", ref _searchText, 256))
            UpdateFilter();

        if (_filteredExpansions.Count > 0)
        {
            using var table = ImRaii.Table("GatheringPoints", 3, ImGuiTableFlags.NoSavedSettings);
            if (!table)
                return;

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoHide);
            ImGui.TableSetupColumn("Supported", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableSetupColumn("Collected", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableHeadersRow();

            foreach (var expansion in _filteredExpansions)
                DrawExpansion(expansion);
        }
        else
            ImGui.Text("No area, gathering point or item matches your search text.");
    }

    private void DrawExpansion(FilteredExpansion expansion)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool open = ImGui.TreeNodeEx(expansion.Expansion.ExpansionVersion.ToFriendlyString(),
            ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        DrawCount(expansion.Expansion.CompletedPoints, expansion.Expansion.TotalPoints);
        ImGui.TableNextColumn();
        DrawCount(expansion.Expansion.CompletedItems, expansion.Expansion.TotalItems);

        if (open)
        {
            foreach (var territory in expansion.Territories)
                DrawTerritory(territory);

            ImGui.TreePop();
        }
    }

    private void DrawTerritory(FilteredTerritory territory)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool open = ImGui.TreeNodeEx(territory.Territory.ToFriendlyString(), ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        DrawCount(territory.Territory.CompletedPoints, territory.Territory.TotalPoints);
        ImGui.TableNextColumn();
        DrawCount(territory.Territory.CompletedItems, territory.Territory.TotalItems);

        if (open)
        {
            foreach (var point in territory.GatheringPoints)
                DrawPoint(point);

            ImGui.TreePop();
        }
    }

    private void DrawPoint(FilteredGatheringPoint point)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool open = ImGui.TreeNodeEx($"{point.Point.PlaceName} ({point.Point.ClassJob} Lv. {point.Point.Level})",
            ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        float spacing;
        // ReSharper disable once UnusedVariable
        using (var font = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
        _uiUtils.ChecklistItem(string.Empty, point.Point.IsComplete);

        ImGui.TableNextColumn();
        DrawCount(point.Point.CompletedItems, point.Point.TotalItems);

        if (open)
        {
            foreach (var item in point.GatheringItemIds)
                DrawItem(item);

            ImGui.TreePop();
        }
    }

    private void DrawItem(ushort item)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TreeNodeEx(_gatheringItems.GetValueOrDefault(item, "???"),
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();

        ImGui.TableNextColumn();
        float spacing;
        // ReSharper disable once UnusedVariable
        using (var font = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
        if (item < 10_000)
            _uiUtils.ChecklistItem(string.Empty, _gatheredItems.Contains(item));
        else
            _uiUtils.ChecklistItem(string.Empty, ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus);
    }

    private static void DrawCount(int count, int total)
    {
        string len = 999.ToString(CultureInfo.CurrentCulture);
        ImGui.PushFont(UiBuilder.MonoFont);

        string text =
            $"{count.ToString(CultureInfo.CurrentCulture).PadLeft(len.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadLeft(len.Length)}";
        if (count == total)
            ImGui.TextColored(ImGuiColors.ParsedGreen, text);
        else
            ImGui.TextUnformatted(text);

        ImGui.PopFont();
    }

    public void UpdateFilter()
    {
        Predicate<string> match;
        if (string.IsNullOrWhiteSpace(_searchText))
            match = _ => true;
        else
            match = x => x.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);

        _filteredExpansions = _gatheringPointsByExpansion
            .Select(section => FilterExpansion(section, match))
            .Where(x => x != null)
            .Cast<FilteredExpansion>()
            .ToList();
    }

    private FilteredExpansion? FilterExpansion(ExpansionPoints expansion, Predicate<string> match)
    {
        List<FilteredTerritory> filteredTerritories = expansion.PointsByTerritories
            .Select(x => FilterTerritory(x, match))
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();
        if (filteredTerritories.Count > 0)
            return new FilteredExpansion(expansion, filteredTerritories);

        return null;
    }

    private FilteredTerritory? FilterTerritory(TerritoryPoints territory, Predicate<string> match)
    {
        if (match(territory.TerritoryName))
        {
            return new FilteredTerritory(territory,
                territory.Points
                    .Select(x => FilterGatheringPoint(x, _ => true)!)
                    .ToList());
        }
        else
        {
            List<FilteredGatheringPoint> filteredPoints = territory.Points
                .Select(x => FilterGatheringPoint(x, match))
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
            if (filteredPoints.Count > 0)
                return new FilteredTerritory(territory, filteredPoints);
        }

        return null;
    }

    private FilteredGatheringPoint? FilterGatheringPoint(DefaultGatheringPoint gatheringPoint,
        Predicate<string> match)
    {
        if (match(gatheringPoint.PlaceName ?? string.Empty))
            return new FilteredGatheringPoint(gatheringPoint, gatheringPoint.GatheringItemIds);
        else
        {
            List<ushort> filteredItems = gatheringPoint.GatheringItemIds
                .Where(x => match(_gatheringItems.GetValueOrDefault(x, string.Empty))).ToList();
            if (filteredItems.Count > 0)
                return new FilteredGatheringPoint(gatheringPoint, filteredItems);
        }

        return null;
    }

    internal void RefreshCounts()
    {
        _gatheredItems.Clear();
        foreach (ushort key in _gatheringItems.Keys)
        {
            if (IsGatheringItemGathered(key))
                _gatheredItems.Add(key);
        }

        foreach (var expansion in _gatheringPointsByExpansion)
        {
            foreach (var territory in expansion.PointsByTerritories)
            {
                foreach (var point in territory.Points)
                {
                    point.TotalItems = point.GatheringItemIds.Count(x => x < 10_000);
                    point.CompletedItems = point.GatheringItemIds.Count(_gatheredItems.Contains);
                    point.IsComplete = _gatheringPointRegistry.TryGetGatheringPoint(point.Id, out _);
                }

                territory.TotalItems = territory.Points.Sum(x => x.TotalItems);
                territory.CompletedItems = territory.Points.Sum(x => x.CompletedItems);
                territory.CompletedPoints = territory.Points.Count(x => x.IsComplete);
            }

            expansion.TotalItems = expansion.PointsByTerritories.Sum(x => x.TotalItems);
            expansion.CompletedItems = expansion.PointsByTerritories.Sum(x => x.CompletedItems);
            expansion.TotalPoints = expansion.PointsByTerritories.Sum(x => x.TotalPoints);
            expansion.CompletedPoints = expansion.PointsByTerritories.Sum(x => x.CompletedPoints);
        }
    }

    public void ClearCounts(int type, int code)
    {
        foreach (var expansion in _gatheringPointsByExpansion)
        {
            expansion.CompletedItems = 0;
            expansion.CompletedPoints = 0;

            foreach (var territory in expansion.PointsByTerritories)
            {
                territory.CompletedItems = 0;
                territory.CompletedPoints = 0;

                foreach (var point in territory.Points)
                {
                    point.IsComplete = false;
                }
            }
        }
    }

    private sealed record ExpansionPoints(EExpansionVersion ExpansionVersion, List<TerritoryPoints> PointsByTerritories)
    {
        public int TotalItems { get; set; }
        public int TotalPoints { get; set; }
        public int CompletedItems { get; set; }
        public int CompletedPoints { get; set; }
    }

    private sealed record TerritoryPoints(
        ushort TerritoryType,
        string TerritoryName,
        List<DefaultGatheringPoint> Points)
    {
        public int TotalItems { get; set; }
        public int TotalPoints => Points.Count;
        public int CompletedItems { get; set; }
        public int CompletedPoints { get; set; }

        public string ToFriendlyString() =>
            !string.IsNullOrEmpty(TerritoryName) ? TerritoryName : $"??? ({TerritoryType})";
    }

    private sealed record DefaultGatheringPoint(
        GatheringPointId Id,
        EClassJob ClassJob,
        byte Level,
        List<ushort> GatheringItemIds,
        EExpansionVersion Expansion,
        ushort TerritoryType,
        string? TerritoryName,
        string? PlaceName)
    {
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public bool IsComplete { get; set; }
    }

    private sealed record FilteredExpansion(ExpansionPoints Expansion, List<FilteredTerritory> Territories);

    private sealed record FilteredTerritory(TerritoryPoints Territory, List<FilteredGatheringPoint> GatheringPoints);

    private sealed record FilteredGatheringPoint(DefaultGatheringPoint Point, List<ushort> GatheringItemIds);
}
