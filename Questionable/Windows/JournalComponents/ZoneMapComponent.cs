using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class ZoneMapComponent(
    IDataManager dataManager,
    ITextureProvider textureProvider,
    IClientState clientState,
    QuestData questData,
    QuestRegistry questRegistry,
    UiUtils uiUtils,
    QuestTooltipComponent questTooltipComponent,
    QuestJournalUtils questJournalUtils)
{
    private const string Source = "ZoneMap";

    private sealed record Zone(uint TerritoryId, string Name, Map Map, List<(QuestInfo Quest, Vector2 Position)> Quests);

    private readonly Lazy<(List<Zone> Zones, uint[] Ids, string[] Labels)> _data = new(() => Build(dataManager, questData));
    private uint? _territoryId;
    private string _search = string.Empty;

    private static (List<Zone>, uint[], string[]) Build(IDataManager dataManager, QuestData questData)
    {
        ExcelSheet<TerritoryType> territories = dataManager.GetExcelSheet<TerritoryType>();
        Dictionary<uint, Zone> zones = [];
        foreach (QuestInfo quest in questData.GetAll().OfType<QuestInfo>())
        {
            QuestInfo.SheetLevel issuer = quest.IssuerLocation;
            if (territories.GetRowOrDefault(issuer.TerritoryId) is not { RowId: > 0, Map.RowId: > 0 } territory)
                continue;

            if (!zones.TryGetValue(territory.RowId, out Zone? zone))
            {
                string name = territory.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
                if (name.Length == 0)
                    continue;
                zone = new(territory.RowId, name, territory.Map.Value, []);
                zones[territory.RowId] = zone;
            }

            zone.Quests.Add((quest, new Vector2(issuer.X, issuer.Z)));
        }

        List<Zone> sorted = [.. zones.Values.OrderBy(x => x.Name, StringComparer.CurrentCulture)];
        return (sorted, [.. sorted.Select(x => x.TerritoryId)],
            [.. sorted.Select(x => _LF("{0} ({1} quests)", x.Name, x.Quests.Count))]);
    }

    public void Draw()
    {
        (List<Zone> zones, uint[] ids, string[] labels) = _data.Value;
        if (zones.Count == 0)
            return;

        _territoryId ??= zones.Any(x => x.TerritoryId == clientState.TerritoryType)
            ? clientState.TerritoryType
            : zones[0].TerritoryId;
        Zone zone = zones.FirstOrDefault(x => x.TerritoryId == _territoryId) ?? zones[0];

        ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X, 300 * ImGuiHelpers.GlobalScale));
        uint territoryId = zone.TerritoryId;
        if (ImGuiComponentsLocal.DrawSearchableCombo("##Zone", ids, labels, zone.TerritoryId, ref _search, ref territoryId))
            _territoryId = territoryId;

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.LocationArrow, _L("My zone")))
            _territoryId = zones.Any(x => x.TerritoryId == clientState.TerritoryType) ? clientState.TerritoryType : _territoryId;

        DrawMap(zone);
    }

    private void DrawMap(Zone zone)
    {
        string mapId = zone.Map.Id.ToString();
        var texture = textureProvider.GetFromGame($"ui/map/{mapId}/{mapId.Replace("/", "", StringComparison.Ordinal)}_m.tex")
            .GetWrapOrEmpty();

        Vector2 available = ImGui.GetContentRegionAvail();
        float size = Math.Max(100f, Math.Min(available.X, available.Y));
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImGui.Image(texture.Handle, new Vector2(size));

        float scale = zone.Map.SizeFactor / 100f;
        float dot = 5f * ImGuiHelpers.GlobalScale;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        foreach ((QuestInfo quest, Vector2 position) in zone.Quests)
        {
            // World to 0..1 across the 2048-unit map texture.
            Vector2 normalized = new(
                ((position.X + zone.Map.OffsetX) * scale + 1024f) / 2048f,
                ((position.Y + zone.Map.OffsetY) * scale + 1024f) / 2048f);
            Vector2 center = origin + normalized * size;
            (Vector4 color, FontAwesomeIcon _, string _) = uiUtils.GetQuestStyle(quest.QuestId);
            drawList.AddCircleFilled(center, dot, QstTheme.ToU32(color));
            drawList.AddCircle(center, dot, QstTheme.ToU32(QstTheme.PanelDark));

            using ImRaii.IdDisposable id = ImRaii.PushId(quest.QuestId.ToString());
            ImGui.SetCursorScreenPos(center - new Vector2(dot));
            ImGui.InvisibleButton("##Quest", new Vector2(dot * 2));
            if (ImGui.IsItemHovered())
                questTooltipComponent.Draw(quest);

            questRegistry.TryGetQuest(quest.QuestId, out Quest? path);
            questJournalUtils.ShowContextMenu(quest, path, Source);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, size));
        ImGui.Dummy(Vector2.Zero);
    }
}
