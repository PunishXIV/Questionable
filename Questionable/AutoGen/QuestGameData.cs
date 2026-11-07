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

internal sealed class QuestGameData : IDisposable
{
    // Journal quest names can start with these icon characters.
    private const char QuestSyncIcon = (char)0xE0BE;
    private const char QuestRepeatableIcon = (char)0xE0BF;

    private readonly GameData _gameData;
    private readonly bool _disposeGameData;
    private readonly Lazy<ILookup<uint, Level>> _levelsByObject;
    private readonly Lazy<IReadOnlyList<AetheryteEntry>> _aetherytes;
    private readonly Lazy<ILookup<uint, uint>> _followers;
    private readonly Lazy<Dictionary<string, EEmote>> _emotesByCommand;

    // Cache layer data per territory; a quest touches few zones.
    private readonly Dictionary<uint, Dictionary<uint, List<Vector3>>> _layerPlacements = [];

    // Quest actors are usually in planevent; map-marker actors can be in the other files.
    private static readonly string[] LayerFiles = ["planevent", "planmap", "planlive"];

    internal QuestGameData(string sqPackDirectory)
        : this(new GameData(sqPackDirectory, new LuminaOptions
        {
            PanicOnSheetChecksumMismatch = false,
            DefaultExcelLanguage = Language.English
        }), disposeGameData: true)
    {
    }

    // Borrows Dalamud's GameData, so it must not be disposed here.
    internal QuestGameData(GameData gameData)
        : this(gameData, disposeGameData: false)
    {
    }

    private QuestGameData(GameData gameData, bool disposeGameData)
    {
        _gameData = gameData;
        _disposeGameData = disposeGameData;

        _levelsByObject = new Lazy<ILookup<uint, Level>>(BuildLevelIndex);
        _aetherytes = new Lazy<IReadOnlyList<AetheryteEntry>>(BuildAetheryteIndex);
        _followers = new Lazy<ILookup<uint, uint>>(BuildFollowerIndex);
        _emotesByCommand = new Lazy<Dictionary<string, EEmote>>(BuildEmoteIndex);
    }

    internal ExcelSheet<Quest> Quests => _gameData.GetExcelSheet<Quest>()
                                       ?? throw new InvalidOperationException("Quest sheet unavailable.");

    internal string GameVersion =>
        _gameData.Repositories.TryGetValue("ffxiv", out var repository) ? repository.Version : "unknown";

    internal Quest? FindByQuestId(ushort questId)
    {
        // Journal ids omit the Quest row's high bits.
        foreach (Quest quest in Quests)
        {
            if ((quest.RowId & 0xFFFF) == questId && !quest.Name.IsEmpty)
                return quest;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposeGameData)
            _gameData.Dispose();
    }

    internal IEnumerable<Quest> PreviousQuests(Quest quest)
    {
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && Quests.GetRowOrDefault(previous.RowId) is { } row && !row.Name.IsEmpty)
                yield return row;
        }
    }

    internal IEnumerable<Quest> NextQuests(Quest quest)
    {
        foreach (uint rowId in _followers.Value[quest.RowId])
        {
            if (Quests.GetRowOrDefault(rowId) is { } row && !row.Name.IsEmpty)
                yield return row;
        }
    }

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

    internal IReadOnlyList<Quest> FindByName(string name)
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

    internal static string QuestName(Quest quest) => TrimJournalIcons(quest.Name.ExtractText());

    private static string TrimJournalIcons(string name) =>
        name.TrimStart(QuestSyncIcon, QuestRepeatableIcon, ' ');

    internal LuaProto? LoadScript(Quest quest)
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

    internal IEnumerable<Level> LevelsForObject(uint dataId) => _levelsByObject.Value[dataId];

    internal IReadOnlyList<Vector3> LayerPositions(uint dataId, uint territoryId) =>
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

    internal Level? FindLevel(uint levelRowId)
    {
        if (levelRowId == 0)
            return null;

        Level? level = _gameData.GetExcelSheet<Level>()?.GetRowOrDefault(levelRowId);
        return level is { Territory.RowId: > 0 } ? level : null;
    }

    internal bool TerritorySupportsFlight(uint territoryId)
    {
        TerritoryType? territory = _gameData.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory is { AetherCurrentCompFlgSet.RowId: > 0 };
    }

    internal string TerritoryName(uint territoryId)
    {
        TerritoryType? territory = _gameData.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? $"#{territoryId}";
    }

    internal IReadOnlyList<string> TodoTexts(Quest quest)
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

    internal EEmote? EmoteFromCommand(string command)
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

    internal string EventItemName(uint itemId) =>
        _gameData.GetExcelSheet<EventItem>()?.GetRowOrDefault(itemId)?.Singular.ExtractText() ?? string.Empty;

    internal int EventItemStackSize(uint itemId) =>
        _gameData.GetExcelSheet<EventItem>()?.GetRowOrDefault(itemId)?.StackSize is { } size and > 0
            ? (int)size
            : 0;

    internal string ObjectName(uint dataId)
    {
        if (dataId == 0)
            return string.Empty;

        if (dataId >= 2_000_000)
            return _gameData.GetExcelSheet<EObjName>()?.GetRowOrDefault(dataId)?.Singular.ExtractText() ?? string.Empty;

        return _gameData.GetExcelSheet<ENpcResident>()?.GetRowOrDefault(dataId)?.Singular.ExtractText() ?? string.Empty;
    }

    internal TravelShortcut? ResolveTravel(uint territoryId, Vector3 position)
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

    internal Vector2? AetherytePosition(EAetheryteLocation location)
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

    // The Level rows the Aetheryte sheet points at no longer exist; MapMarker still has every aetheryte
    // (DataType 3) and aethernet shard (DataType 4), in map pixels.
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

                if (aetheryteId is not { } rowId || rowId == 0)
                    continue;

                float scale = map.SizeFactor / 100f;
                Vector2 world = new(
                    (marker.X - 1024f) / scale - map.OffsetX,
                    (marker.Y - 1024f) / scale - map.OffsetY);

                // Region maps repeat a zone's markers; the map covering the aetheryte's own territory wins.
                bool preferred = map.TerritoryType.RowId != 0 &&
                                 map.TerritoryType.RowId == aetherytes.GetRowOrDefault(rowId)?.Territory.RowId;

                if (preferred || !positions.ContainsKey(rowId))
                    positions[rowId] = world;
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
