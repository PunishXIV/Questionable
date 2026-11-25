using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class ContentProgressComponent(TerritoryData territoryData, GameFunctions gameFunctions, UiUtils uiUtils)
{
    private static readonly (EContentType Type, string Name)[] Types =
    [
        (EContentType.Dungeons, _L("Dungeons")),
        (EContentType.Guildhests, _L("Guildhests")),
        (EContentType.Trials, _L("Trials")),
        (EContentType.Raids, _L("Raids")),
        (EContentType.ChaoticRaid, _L("Chaotic raids")),
        (EContentType.UltimateRaids, _L("Ultimate raids")),
        (EContentType.Variant, _L("Variant and criterion dungeons")),
    ];

    private readonly Lazy<ILookup<EContentType, (TerritoryData.CfcData Cfc, uint ContentId)>> _duties = new(() =>
        territoryData.AllCfcs
            .Where(x => x.ContentType != null && !string.IsNullOrEmpty(x.Name) &&
                        gameFunctions.CfcToContentId.ContainsKey(x.Id) && !IsHidden(x.Id))
            .OrderBy(x => x.RequiredLevel).ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToLookup(x => x.ContentType!.Value, x => (x, gameFunctions.CfcToContentId[x.Id])));

    // Sort key 9999 marks a row the Duty Finder hides (an old copy of the Gilded Araya).
    private static bool IsHidden(uint cfcId) =>
        Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>().GetRowOrDefault(cfcId)?.SortKey == 9999;

    private readonly JournalRefresh _refresh = new(TimeSpan.FromSeconds(1));
    private readonly Dictionary<EContentType, (List<(TerritoryData.CfcData Cfc, uint ContentId)> Duties, int Unlocked, int Completed)> _counts = [];
    private string _filter = string.Empty;
    private string? _countedFilter;

    public void Draw()
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##ContentFilter", _L("Search duties"), ref _filter, 100);
        if (_filter != _countedFilter)
            Recount();
        else
            _refresh.Tick(DateTime.Now, Recount);

        using ImRaii.TableDisposable table = ImRaii.Table("Content", 3, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        float scale = ImGui.GetIO().FontGlobalScale;
        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn(_L("Unlocked"), ImGuiTableColumnFlags.WidthFixed, 125 * scale);
        ImGui.TableSetupColumn(_L("Completed"), ImGuiTableColumnFlags.WidthFixed, 125 * scale);
        ImGui.TableHeadersRow();

        foreach ((EContentType type, string name) in Types)
            DrawType(type, name);
    }

    private bool Recount()
    {
        _countedFilter = _filter;
        foreach ((EContentType type, string _) in Types)
        {
            List<(TerritoryData.CfcData Cfc, uint ContentId)> duties = [.. _duties.Value[type]
                .Where(x => _filter.Length == 0 || x.Cfc.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase))];
            _counts[type] = (duties, duties.Count(x => UIState.IsInstanceContentUnlocked(x.ContentId)),
                duties.Count(x => UIState.IsInstanceContentCompleted(x.ContentId)));
        }

        return true;
    }

    private void DrawType(EContentType type, string name)
    {
        if (!_counts.TryGetValue(type, out var counts) || counts.Duties.Count == 0)
            return;

        List<(TerritoryData.CfcData Cfc, uint ContentId)> duties = counts.Duties;
        JournalTable.BeginRow();
        bool open = ImGui.TreeNodeEx(name, ImGuiTreeNodeFlags.SpanFullWidth);
        ImGui.TableNextColumn();
        JournalTable.DrawCount(counts.Unlocked, duties.Count);
        ImGui.TableNextColumn();
        JournalTable.DrawCount(counts.Completed, duties.Count);
        if (!open)
            return;

        foreach ((TerritoryData.CfcData cfc, uint contentId) in duties)
        {
            JournalTable.BeginRow();
            ImGui.TreeNodeEx($"{cfc.Name} ({cfc.RequiredLevel})###{cfc.Id}",
                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
            ImGui.TableNextColumn();
            uiUtils.ChecklistItem("", UIState.IsInstanceContentUnlocked(contentId));
            ImGui.TableNextColumn();
            uiUtils.ChecklistItem("", UIState.IsInstanceContentCompleted(contentId));
        }

        ImGui.TreePop();
    }
}
