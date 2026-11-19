using Lumina.Excel.Sheets;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class TravelUnlockComponent(
    IDataManager dataManager,
    AetheryteData aetheryteData,
    AetheryteFunctions aetheryteFunctions,
    GameFunctions gameFunctions)
{
    private sealed record Zone(uint TerritoryId, string Name, List<(EAetheryteLocation Id, string Name)> Aetherytes,
        List<(EAetheryteLocation Id, string Name)> Shards, List<(uint Id, string? Quest)> Currents);

    private readonly JournalRefresh _refresh = new(TimeSpan.FromSeconds(1));
    private readonly Dictionary<uint, Status> _status = [];

    private readonly Lazy<List<(string Expansion, List<Zone> Zones)>> _expansions = new(() => Build(dataManager, aetheryteData));

    private static List<(string, List<Zone>)> Build(IDataManager dataManager, AetheryteData aetheryteData)
    {
        Dictionary<uint, Zone> zones = [];
        Zone ZoneFor(uint territoryId)
        {
            if (!zones.TryGetValue(territoryId, out Zone? zone))
            {
                TerritoryType? territory = dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
                zone = new(territoryId, territory?.PlaceName.ValueNullable?.Name.ToString() ?? territoryId.ToString(CultureInfo.InvariantCulture),
                    [], [], []);
                zones[territoryId] = zone;
            }

            return zone;
        }

        foreach (Aetheryte aetheryte in dataManager.GetExcelSheet<Aetheryte>())
        {
            EAetheryteLocation id = (EAetheryteLocation)aetheryte.RowId;
            if (!Enum.IsDefined(id) || !aetheryteData.TerritoryIds.TryGetValue(id, out uint territoryId))
                continue;

            string? name = (aetheryte.IsAetheryte ? aetheryte.PlaceName.ValueNullable?.Name : aetheryte.AethernetName.ValueNullable?.Name)
                ?.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            Zone zone = ZoneFor(territoryId);
            (aetheryte.IsAetheryte ? zone.Aetherytes : zone.Shards).Add((id, name));
        }

        foreach (AetherCurrentCompFlgSet set in dataManager.GetExcelSheet<AetherCurrentCompFlgSet>())
        {
            if (set.Territory.RowId == 0)
                continue;

            Zone zone = ZoneFor(set.Territory.RowId);
            foreach (RowRef<AetherCurrent> current in set.AetherCurrents.Where(x => x.RowId > 0))
                zone.Currents.Add((current.RowId, current.ValueNullable?.Quest.ValueNullable?.Name.ToString()));
        }

        ExcelSheet<ExVersion> expansions = dataManager.GetExcelSheet<ExVersion>();
        return [.. zones
            .GroupBy(x => dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(x.Key)?.ExVersion.RowId ?? 0)
            .OrderBy(x => x.Key)
            .Select(x => (expansions.GetRowOrDefault(x.Key)?.Name.ToString() ?? string.Empty,
                x.Select(y => y.Value).OrderBy(y => y.Name, StringComparer.CurrentCulture).ToList()))];
    }

    public void Draw()
    {
        using ImRaii.TableDisposable table = ImRaii.Table("TravelUnlocks", 4, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        float scale = ImGui.GetIO().FontGlobalScale;
        ImGui.TableSetupColumn(_L("Zone"), ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn(_L("Aetherytes"), ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn(_L("Aethernet"), ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn(_L("Aether currents"), ImGuiTableColumnFlags.WidthFixed, 125 * scale);
        ImGui.TableHeadersRow();

        _refresh.Tick(DateTime.Now, Recount);
        foreach ((string expansion, List<Zone> zones) in _expansions.Value)
        {
            JournalTable.BeginRow();
            bool open = ImGui.TreeNodeEx(expansion, ImGuiTreeNodeFlags.SpanFullWidth);
            DrawCounts(zones, zones.Select(x => _status.GetValueOrDefault(x.TerritoryId) ?? Status.Empty).ToList());
            if (!open)
                continue;

            foreach (Zone zone in zones)
                DrawZone(zone, _status.GetValueOrDefault(zone.TerritoryId) ?? Status.Empty);
            ImGui.TreePop();
        }
    }

    private bool Recount()
    {
        foreach (Zone zone in _expansions.Value.SelectMany(x => x.Zones))
        {
            _status[zone.TerritoryId] = new(
                zone.Aetherytes.Count(x => aetheryteFunctions.IsAetheryteUnlocked(x.Id)),
                zone.Shards.Count(x => aetheryteFunctions.IsAetheryteUnlocked(x.Id)),
                zone.Currents.Count(x => gameFunctions.IsAetherCurrentUnlocked(x.Id)),
                [
                    .. zone.Aetherytes.Concat(zone.Shards).Where(x => !aetheryteFunctions.IsAetheryteUnlocked(x.Id)).Select(x => x.Name),
                    .. zone.Currents.Where(x => x.Quest != null && !gameFunctions.IsAetherCurrentUnlocked(x.Id)).Select(x => x.Quest!),
                ],
                zone.Currents.Count(x => x.Quest == null && !gameFunctions.IsAetherCurrentUnlocked(x.Id)));
        }

        return true;
    }

    private static void DrawZone(Zone zone, Status status)
    {
        JournalTable.BeginRow();
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (status.Missing.Count == 0 && status.MissingField == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;
        bool open = ImGui.TreeNodeEx($"{zone.Name}###{zone.TerritoryId}", flags);
        DrawCounts([zone], [status]);
        if (!open)
            return;

        foreach (string name in status.Missing)
        {
            JournalTable.BeginRow();
            ImGui.TextColored(QstTheme.TextMuted, name);
        }

        if (status.MissingField > 0)
        {
            JournalTable.BeginRow();
            ImGui.TextColored(QstTheme.TextMuted, _LF("{0} field aether currents still to find", status.MissingField));
        }

        ImGui.TreePop();
    }

    private static void DrawCounts(List<Zone> zones, List<Status> statuses)
    {
        ImGui.TableNextColumn();
        JournalTable.DrawCount(statuses.Sum(x => x.Aetherytes), zones.Sum(x => x.Aetherytes.Count));
        ImGui.TableNextColumn();
        JournalTable.DrawCount(statuses.Sum(x => x.Shards), zones.Sum(x => x.Shards.Count));
        ImGui.TableNextColumn();
        JournalTable.DrawCount(statuses.Sum(x => x.Currents), zones.Sum(x => x.Currents.Count));
    }

    private sealed record Status(int Aetherytes, int Shards, int Currents, List<string> Missing, int MissingField)
    {
        public static Status Empty { get; } = new(0, 0, 0, [], 0);
    }
}
