// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using Questionable.AutoGen.Generation;
using Questionable.AutoGen.Lua;
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
    private readonly Lazy<Dictionary<string, EEmote>> _emotesByCommand;

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
        _emotesByCommand = new Lazy<Dictionary<string, EEmote>>(BuildEmoteIndex);
    }

    internal ExcelSheet<Quest> Quests => _gameData.GetExcelSheet<Quest>()
                                       ?? throw new InvalidOperationException("Quest sheet unavailable.");

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
                // Not every zone has every layer file, and a few fail to parse.
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

    internal IReadOnlyList<string> TodoTexts(Quest quest)
    {
        string id = quest.Id.ExtractText();
        int underscore = id.LastIndexOf('_');
        if (underscore < 0 || underscore + 4 > id.Length)
            return [];

        ExcelSheet<QuestDialogueText>? sheet;
        try
        {
            sheet = _gameData.Excel.GetSheet<QuestDialogueText>(name: $"quest/{id.Substring(underscore + 1, 3)}/{id}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }

        if (sheet == null)
            return [];

        SortedDictionary<int, string> byIndex = [];
        foreach (QuestDialogueText row in sheet)
        {
            string key = row.Key.ExtractText();
            int marker = key.IndexOf("_TODO_", StringComparison.Ordinal);
            if (marker < 0 || !int.TryParse(key[(marker + "_TODO_".Length)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                continue;

            byIndex[index] = row.Value.ExtractText();
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

    private ILookup<uint, Level> BuildLevelIndex()
    {
        ExcelSheet<Level>? levels = _gameData.GetExcelSheet<Level>();
        if (levels == null)
            return Enumerable.Empty<Level>().ToLookup(_ => 0u);

        return levels
            .Where(x => x.Object.RowId != 0 && x.Territory.RowId != 0)
            .ToLookup(x => x.Object.RowId);
    }
}
