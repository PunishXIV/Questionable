// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using Questionable.AutoGen.Generation;
using Questionable.AutoGen.Lua;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Model.Questing.Converter;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen;

/// <summary>Excel and file lookups the generator needs, backed by a local game install.</summary>
public sealed class QuestGameData : IDisposable
{
    // SeIconChar.QuestSync / SeIconChar.QuestRepeatable, which the journal prefixes some names with.
    private const char QuestSyncIcon = (char)0xE0BE;
    private const char QuestRepeatableIcon = (char)0xE0BF;

    private readonly GameData _gameData;
    private readonly bool _ownsGameData;
    private readonly Lazy<ILookup<uint, Level>> _levelsByObject;
    private readonly Lazy<IReadOnlyList<AetheryteEntry>> _aetherytes;
    private readonly Lazy<ILookup<uint, uint>> _followers;
    private readonly Lazy<Dictionary<string, EEmote>> _emotesByCommand;

    // Layer data is read per territory on first use; a quest only ever touches a handful of zones.
    private readonly Dictionary<uint, Dictionary<uint, List<Vector3>>> _layerPlacements = [];

    // Quest actors live in planevent; planmap and planlive carry a few of the ones tied to map markers.
    private static readonly string[] LayerFiles = ["planevent", "planmap", "planlive"];

    /// <summary>Standalone use: opens the given <c>game/sqpack</c> directory and owns the handle.</summary>
    public QuestGameData(string sqPackDirectory)
        : this(new GameData(sqPackDirectory, new LuminaOptions
        {
            PanicOnSheetChecksumMismatch = false,
            DefaultExcelLanguage = Language.English
        }), ownsGameData: true)
    {
    }

    /// <summary>
    ///     In-plugin use: borrows an already-open <see cref="GameData"/> (Dalamud's, via
    ///     <c>IDataManager.GameData</c>), which must not be disposed by us.
    /// </summary>
    public QuestGameData(GameData gameData)
        : this(gameData, ownsGameData: false)
    {
    }

    private QuestGameData(GameData gameData, bool ownsGameData)
    {
        _gameData = gameData;
        _ownsGameData = ownsGameData;

        _levelsByObject = new Lazy<ILookup<uint, Level>>(BuildLevelIndex);
        _aetherytes = new Lazy<IReadOnlyList<AetheryteEntry>>(BuildAetheryteIndex);
        _followers = new Lazy<ILookup<uint, uint>>(BuildFollowerIndex);
        _emotesByCommand = new Lazy<Dictionary<string, EEmote>>(BuildEmoteIndex);
    }

    public ExcelSheet<Quest> Quests => _gameData.GetExcelSheet<Quest>()
                                       ?? throw new InvalidOperationException("Quest sheet unavailable.");

    public string GameVersion =>
        _gameData.Repositories.TryGetValue("ffxiv", out var repository) ? repository.Version : "unknown";

    /// <summary>Looks a quest up by its in-journal id, the number used in questpath filenames.</summary>
    public Quest? FindByQuestId(ushort questId)
    {
        // Quest rows are 0x10000-based; a bare journal id needs the high bits restored.
        foreach (Quest quest in Quests)
        {
            if ((quest.RowId & 0xFFFF) == questId && !quest.Name.IsEmpty)
                return quest;
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsGameData)
            _gameData.Dispose();
    }

    /// <summary>The quests listed as prerequisites of <paramref name="quest"/>.</summary>
    public IEnumerable<Quest> PreviousQuests(Quest quest)
    {
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && Quests.GetRowOrDefault(previous.RowId) is { } row && !row.Name.IsEmpty)
                yield return row;
        }
    }

    /// <summary>The quests that list <paramref name="quest"/> as one of their prerequisites.</summary>
    public IEnumerable<Quest> NextQuests(Quest quest)
    {
        foreach (uint rowId in _followers.Value[quest.RowId])
        {
            if (Quests.GetRowOrDefault(rowId) is { } row && !row.Name.IsEmpty)
                yield return row;
        }
    }

    /// <summary>Reverse of <c>Quest.PreviousQuest</c>, so the chain can be walked forwards as well as back.</summary>
    private ILookup<uint, uint> BuildFollowerIndex()
    {
        List<(uint Previous, uint Next)> edges = [];
        foreach (Quest quest in Quests)
        {
            if (quest.Name.IsEmpty)
                continue;

            foreach (var previous in quest.PreviousQuest)
            {
                if (previous.RowId != 0)
                    edges.Add((previous.RowId, quest.RowId));
            }
        }

        return edges.ToLookup(x => x.Previous, x => x.Next);
    }

    /// <summary>Exact (case-insensitive) name matches if there are any, otherwise substring matches.</summary>
    public IReadOnlyList<Quest> FindByName(string name)
    {
        List<Quest> exact = [];
        List<Quest> partial = [];

        foreach (Quest quest in Quests)
        {
            string questName = TrimJournalIcons(quest.Name.ExtractText());
            if (questName.Length == 0)
                continue;

            if (string.Equals(questName, name, StringComparison.OrdinalIgnoreCase))
                exact.Add(quest);
            else if (questName.Contains(name, StringComparison.OrdinalIgnoreCase))
                partial.Add(quest);
        }

        return exact.Count > 0 ? exact : partial;
    }

    public static string QuestName(Quest quest) => TrimJournalIcons(quest.Name.ExtractText());

    private static string TrimJournalIcons(string name) =>
        name.TrimStart(QuestSyncIcon, QuestRepeatableIcon, ' ');

    /// <summary>Reads and parses the quest's compiled Lua script, or <c>null</c> when the quest has none.</summary>
    public LuaProto? LoadScript(Quest quest)
    {
        string id = quest.Id.ExtractText();
        int underscore = id.LastIndexOf('_');
        if (underscore < 0 || underscore + 4 > id.Length)
            return null;

        // game_script/quest/<first three digits of the quest number>/<Id>.luab
        string folder = id.Substring(underscore + 1, 3);
        var file = _gameData.GetFile($"game_script/quest/{folder}/{id}.luab");
        if (file == null)
            return null;

        try
        {
            return LuaBytecode.Parse(file.Data);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Every <c>Level</c> row pointing at the given ENpc/EObj data id.</summary>
    public IEnumerable<Level> LevelsForObject(uint dataId) => _levelsByObject.Value[dataId];

    /// <summary>
    ///     Every spot the territory's layer data places the given ENpc/EObj, which is where the ones the
    ///     <c>Level</c> sheet has no row for actually stand. A quest's own actors are laid out per quest
    ///     (<c>QST_SubSea068_001</c> and the like), so an id can appear more than once - the caller picks.
    /// </summary>
    public IReadOnlyList<Vector3> LayerPositions(uint dataId, uint territoryId) =>
        LayerPlacements(territoryId).TryGetValue(dataId, out List<Vector3>? positions) ? positions : [];

    private Dictionary<uint, List<Vector3>> LayerPlacements(uint territoryId)
    {
        if (_layerPlacements.TryGetValue(territoryId, out Dictionary<uint, List<Vector3>>? cached))
            return cached;

        Dictionary<uint, List<Vector3>> placements = [];
        _layerPlacements[territoryId] = placements;

        TerritoryType? territory = _gameData.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        string bg = territory?.Bg.ExtractText() ?? string.Empty;
        int lastSlash = bg.LastIndexOf('/');
        if (lastSlash < 0)
            return placements;

        foreach (string name in LayerFiles)
        {
            LgbFile? lgb;
            try
            {
                lgb = _gameData.GetFile<LgbFile>($"bg/{bg[..lastSlash]}/{name}.lgb");
            }
            catch (Exception)
            {
                // Not every zone has every layer file, and a few of the ones that do fail to parse. Layer data
                // is a fallback for positions the Level sheet is missing, so losing one file is not fatal.
                continue;
            }

            if (lgb == null)
                continue;

            foreach (LayerCommon.Layer layer in lgb.Layers)
            {
                foreach (LayerCommon.InstanceObject instance in layer.InstanceObjects)
                {
                    uint baseId = instance.Object switch
                    {
                        LayerCommon.ENPCInstanceObject enpc => enpc.ParentData.ParentData.BaseId,
                        LayerCommon.EventInstanceObject eobj => eobj.ParentData.BaseId,
                        _ => 0u
                    };

                    if (baseId == 0)
                        continue;

                    Vector3 position = new(instance.Transform.Translation.X, instance.Transform.Translation.Y,
                        instance.Transform.Translation.Z);
                    if (placements.TryGetValue(baseId, out List<Vector3>? existing))
                    {
                        if (!existing.Contains(position))
                            existing.Add(position);
                    }
                    else
                        placements[baseId] = [position];
                }
            }
        }

        return placements;
    }

    public Level? FindLevel(uint levelRowId)
    {
        if (levelRowId == 0)
            return null;

        Level? level = _gameData.GetExcelSheet<Level>()?.GetRowOrDefault(levelRowId);
        return level is { Territory.RowId: > 0 } ? level : null;
    }

    /// <summary>Whether the zone has aether currents at all, which is the offline proxy for "flying is possible here".</summary>
    public bool TerritorySupportsFlight(uint territoryId)
    {
        TerritoryType? territory = _gameData.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory is { AetherCurrentCompFlgSet.RowId: > 0 };
    }

    public string TerritoryName(uint territoryId)
    {
        TerritoryType? territory = _gameData.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? $"#{territoryId}";
    }

    /// <summary>
    ///     The quest's own todo lines, indexed by their <c>TODO_nn</c> number, which lines up with the
    ///     <c>TodoParams</c> array. Emote objectives spell the emote out here and nowhere else:
    ///     "/dance for Aanu Vanu."
    /// </summary>
    public IReadOnlyList<string> TodoTexts(Quest quest)
    {
        string id = quest.Id.ExtractText();
        int underscore = id.LastIndexOf('_');
        if (underscore < 0 || underscore + 4 > id.Length)
            return [];

        ExcelSheet<RawRow>? sheet;
        try
        {
            sheet = _gameData.Excel.GetSheet<RawRow>(name: $"quest/{id.Substring(underscore + 1, 3)}/{id}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }

        if (sheet == null)
            return [];

        SortedDictionary<int, string> byIndex = [];
        foreach (RawRow row in sheet)
        {
            string key = row.ReadStringColumn(0).ExtractText();
            int marker = key.IndexOf("_TODO_", StringComparison.Ordinal);
            if (marker < 0 || !int.TryParse(key[(marker + "_TODO_".Length)..], out int index))
                continue;

            byIndex[index] = row.ReadStringColumn(1).ExtractText();
        }

        return byIndex.Count == 0 ? [] : [.. Enumerable.Range(0, byIndex.Keys.Max() + 1)
            .Select(x => byIndex.TryGetValue(x, out string? text) ? text : string.Empty)];
    }

    /// <summary>Maps a text command such as <c>/dance</c> to the emote it triggers.</summary>
    public EEmote? EmoteFromCommand(string command)
    {
        if (_emotesByCommand.Value.TryGetValue(command, out EEmote emote))
            return emote;

        return null;
    }

    private Dictionary<string, EEmote> BuildEmoteIndex()
    {
        ExcelSheet<Emote>? emotes = _gameData.GetExcelSheet<Emote>();
        if (emotes == null)
            return [];

        Dictionary<string, EEmote> byCommand = new(StringComparer.OrdinalIgnoreCase);
        foreach (Emote emote in emotes)
        {
            if (emote.RowId == 0 || !Enum.IsDefined((EEmote)emote.RowId))
                continue;

            if (emote.TextCommand.ValueNullable is not { } textCommand)
                continue;

            foreach (var candidate in new[] { textCommand.Command, textCommand.ShortCommand, textCommand.Alias })
            {
                string text = candidate.ExtractText();
                if (text.Length > 1)
                    byCommand[text] = (EEmote)emote.RowId;
            }
        }

        return byCommand;
    }

    /// <summary>
    ///     How many of a quest's event item you are asked to collect. The sheet's stack size is exactly that
    ///     limit — you cannot hold more than the objective needs — so it doubles as the required count.
    /// </summary>
    /// <summary>Singular name of an event item, as the journal's todo lines write it ("lice comb").</summary>
    public string EventItemName(uint itemId) =>
        _gameData.GetExcelSheet<EventItem>()?.GetRowOrDefault(itemId)?.Singular.ExtractText() ?? string.Empty;

    public int EventItemStackSize(uint itemId) =>
        _gameData.GetExcelSheet<EventItem>()?.GetRowOrDefault(itemId)?.StackSize is { } size and > 0
            ? (int)size
            : 0;

    public string ObjectName(uint dataId)
    {
        if (dataId == 0)
            return string.Empty;

        if (dataId >= 2_000_000)
            return _gameData.GetExcelSheet<EObjName>()?.GetRowOrDefault(dataId)?.Singular.ExtractText() ?? string.Empty;

        return _gameData.GetExcelSheet<ENpcResident>()?.GetRowOrDefault(dataId)?.Singular.ExtractText() ?? string.Empty;
    }

    /// <summary>
    ///     Works out how to travel to a point: the nearest aetheryte in that zone, or — for city wards that have
    ///     no aetheryte of their own, like Limsa Lominsa Upper Decks — the zone's nearest aethernet shard plus
    ///     the hop to reach it from its aethernet group's main aetheryte.
    /// </summary>
    public TravelShortcut? ResolveTravel(uint territoryId, Vector3 position)
    {
        Vector2 target = new(position.X, position.Z);

        List<AetheryteEntry> mainAetherytes = InTerritory(territoryId, main: true);
        (AetheryteEntry? aetheryte, bool aetheryteConfident) = Choose(mainAetherytes, target);
        if (aetheryte != null)
        {
            return new TravelShortcut(aetheryte.Value.Location, Aethernet: null,
                aetheryteConfident ? null : Ambiguity(aetheryte.Value, mainAetherytes));
        }

        List<AetheryteEntry> shards = InTerritory(territoryId, main: false);
        (AetheryteEntry? shard, bool shardConfident) = Choose(shards, target);
        if (shard == null)
            return null;

        // Reach the shard from whichever aetheryte anchors its aethernet group.
        AetheryteEntry? hub = _aetherytes.Value
            .Cast<AetheryteEntry?>()
            .FirstOrDefault(x => x!.Value.IsMainAetheryte &&
                                 x.Value.AethernetGroup != 0 &&
                                 x.Value.AethernetGroup == shard.Value.AethernetGroup);

        if (hub is not { } origin || !IsAethernetEndpoint(origin.Location) ||
            !IsAethernetEndpoint(shard.Value.Location))
            return null;

        // Always emit the hop, even when the shard is a guess: the plugin cannot path between zones, so landing
        // at the wrong shard inside the target zone still works out, whereas stopping at the hub strands it.
        return new TravelShortcut(
            origin.Location,
            new AethernetShortcut { From = origin.Location, To = shard.Value.Location },
            shardConfident ? null : Ambiguity(shard.Value, shards));
    }

    /// <summary>
    ///     Picks the entry nearest <paramref name="target"/>. Reports low confidence when the choice had to be
    ///     made between several candidates whose positions are unknown — not every aethernet shard has a map
    ///     marker (the Idyllshire gates into the Dravanian Hinterlands, for instance), and guessing between two
    ///     unplaced shards is how you end up at the wrong end of the zone.
    /// </summary>
    private static (AetheryteEntry? Entry, bool Confident) Choose(List<AetheryteEntry> candidates, Vector2 target)
    {
        switch (candidates.Count)
        {
            case 0:
                return (null, false);
            case 1:
                return (candidates[0], true);
        }

        // Some entries are never drawn on a map (airship landings, the Idyllshire gates). Choosing among the
        // ones that are placed is fine; it is only a guess when none of them are.
        List<AetheryteEntry> placed = [.. candidates.Where(x => x.Position != null)];
        if (placed.Count == 0)
            return (candidates[0], false);

        AetheryteEntry best = placed[0];
        float bestDistance = Vector2.Distance(best.Position!.Value, target);
        foreach (AetheryteEntry entry in placed.Skip(1))
        {
            float distance = Vector2.Distance(entry.Position!.Value, target);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = entry;
        }

        return (best, true);
    }

    private static string Ambiguity(AetheryteEntry chosen, List<AetheryteEntry> candidates)
    {
        string others = string.Join(", ", candidates
            .Where(x => x.Location != chosen.Location)
            .Select(x => x.Location));

        return $"guessed {chosen.Location} - none of this zone's shards are placed on a map, " +
               $"so verify it against {others}";
    }

    /// <summary>
    ///     Where an aetheryte or aethernet shard stands, in world X/Z. Null when it carries no map marker to
    ///     recover a position from.
    /// </summary>
    public Vector2? AetherytePosition(EAetheryteLocation location)
    {
        foreach (AetheryteEntry entry in _aetherytes.Value)
        {
            if (entry.Location == location)
                return entry.Position;
        }

        return null;
    }

    private List<AetheryteEntry> InTerritory(uint territoryId, bool main) =>
        [.. _aetherytes.Value.Where(x => x.TerritoryId == territoryId && x.IsMainAetheryte == main)];

    /// <summary>Both ends of an aethernet hop have to be names the questpath schema knows.</summary>
    private static bool IsAethernetEndpoint(EAetheryteLocation location) =>
        AethernetShardConverter.Values.ContainsKey(location);

    private ILookup<uint, Level> BuildLevelIndex()
    {
        ExcelSheet<Level>? levels = _gameData.GetExcelSheet<Level>();
        if (levels == null)
            return Enumerable.Empty<Level>().ToLookup(_ => 0u);

        return levels
            .Where(x => x.Object.RowId != 0 && x.Territory.RowId != 0)
            .ToLookup(x => x.Object.RowId);
    }

    private List<AetheryteEntry> BuildAetheryteIndex()
    {
        ExcelSheet<Aetheryte>? aetherytes = _gameData.GetExcelSheet<Aetheryte>();
        if (aetherytes == null)
            return [];

        Dictionary<uint, Vector2> positions = BuildAetherytePositions(aetherytes);

        List<AetheryteEntry> entries = [];
        foreach (EAetheryteLocation location in Enum.GetValues<EAetheryteLocation>())
        {
            if (location == EAetheryteLocation.None)
                continue;

            Aetheryte? aetheryte = aetherytes.GetRowOrDefault((uint)location);
            if (aetheryte is not { Territory.RowId: > 0 })
                continue;

            entries.Add(new AetheryteEntry(
                location,
                aetheryte.Value.Territory.RowId,
                aetheryte.Value.AethernetGroup,
                aetheryte.Value.IsAetheryte,
                positions.TryGetValue((uint)location, out Vector2 position) ? position : null));
        }

        return entries;
    }

    /// <summary>
    ///     Aetheryte world positions, recovered from map markers.
    ///     <para>
    ///         The <c>Level</c> rows the <c>Aetheryte</c> sheet points at no longer exist, but <c>MapMarker</c>
    ///         still carries every aetheryte (<c>DataType</c> 3, keyed by aetheryte row) and every aethernet
    ///         shard (<c>DataType</c> 4, keyed by the <c>PlaceName</c> in <c>Aetheryte.AethernetName</c>).
    ///         Marker coordinates are map pixels, so they convert back to world X/Z through the map's size
    ///         factor and offset — accurate to a couple of units, which is ample for picking the nearest one.
    ///     </para>
    /// </summary>
    private Dictionary<uint, Vector2> BuildAetherytePositions(ExcelSheet<Aetheryte> aetherytes)
    {
        ExcelSheet<Map>? maps = _gameData.GetExcelSheet<Map>();
        SubrowExcelSheet<MapMarker>? markers = _gameData.GetSubrowExcelSheet<MapMarker>();
        if (maps == null || markers == null)
            return [];

        Dictionary<uint, uint> aetheryteByAethernetName = [];
        foreach (Aetheryte aetheryte in aetherytes)
        {
            if (aetheryte.AethernetName.RowId != 0)
                aetheryteByAethernetName[aetheryte.AethernetName.RowId] = aetheryte.RowId;
        }

        Dictionary<uint, Vector2> positions = [];
        foreach (Map map in maps)
        {
            if (map.MapMarkerRange == 0 || map.SizeFactor == 0)
                continue;

            if (!markers.TryGetRow(map.MapMarkerRange, out SubrowCollection<MapMarker> group))
                continue;

            foreach (MapMarker marker in group)
            {
                uint? aetheryteId = marker.DataType switch
                {
                    3 => marker.DataKey.RowId,
                    4 when aetheryteByAethernetName.TryGetValue(marker.DataKey.RowId, out uint id) => id,
                    _ => null
                };

                if (aetheryteId is not { } id2 || id2 == 0)
                    continue;

                float scale = map.SizeFactor / 100f;
                Vector2 world = new(
                    (marker.X - 1024f) / scale - map.OffsetX,
                    (marker.Y - 1024f) / scale - map.OffsetY);

                // Region maps repeat a zone's markers; the map covering the aetheryte's own territory wins.
                bool preferred = map.TerritoryType.RowId != 0 &&
                                 map.TerritoryType.RowId == aetherytes.GetRowOrDefault(id2)?.Territory.RowId;

                if (preferred || !positions.ContainsKey(id2))
                    positions[id2] = world;
            }
        }

        return positions;
    }

    private readonly record struct AetheryteEntry(
        EAetheryteLocation Location,
        uint TerritoryId,
        ushort AethernetGroup,
        bool IsMainAetheryte,
        Vector2? Position);
}
