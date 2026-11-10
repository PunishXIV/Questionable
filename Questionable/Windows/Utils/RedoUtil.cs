using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Sheets = Lumina.Excel.Sheets;
namespace Questionable.Windows.Utils;

[RegisterSingleton]
internal unsafe sealed class RedoUtil
{
    internal readonly Dictionary<Sheets.QuestRedoChapterUI, RedoCache> Chapters = [];
    internal readonly AgentInterface* QuestRedoHud;
    private readonly GameGuiAdapter _gameGui;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly ILogger<RedoUtil> _logger;
    private Domain.Quest? _unlockQuest;
    private bool _warned;
    internal Domain.Quest? UnlockQuest
    {
        get
        {
            if (_unlockQuest == null && !_questRegistry.TryGetQuest(new QuestId(3759), out _unlockQuest))
            {
                if (EzThrottler.Throttle("RedoUtil.UnlockQuest"))
                {
                    _logger.LogWarning("UnlockQuest could not be set, retrying on next frame");
                }
                _warned = true;
                return null;
            }
            if (_warned)
            {
                _logger.LogDebug("UnlockQuest is now set");
                _warned = false;
            }
            return _unlockQuest;
        }
    }
    internal bool IsLocked
    {
        get
        {
            var quest = UnlockQuest;
            return quest == null || !_questFunctions.IsQuestComplete(quest.Id);
        }
    }

    public RedoUtil(GameGuiAdapter gameGui, QuestRegistry questRegistry, QuestFunctions questFunctions, ILogger<RedoUtil> logger)
    {
        _gameGui = gameGui;
        _questFunctions = questFunctions;
        _questRegistry = questRegistry;
        _logger = logger;
        QuestRedoHud = AgentModule.Instance()->GetAgentByInternalId(AgentId.QuestRedoHud);
        Chapters = [];
        Generate();
    }

    private Stopwatch Generate()
    {
        Stopwatch watch = Stopwatch.StartNew();
        var chapterUi = GenericHelpers.GetSheet<Sheets.QuestRedoChapterUI>();
        foreach (Sheets.QuestRedo redo in GenericHelpers.GetSheet<Sheets.QuestRedo>())
        {
            if (redo.Chapter.RowId == 0)
                continue;
            var chapter = chapterUi.GetRow(redo.Chapter.RowId);
            if (!Chapters.TryGetValue(chapter, out RedoCache? cache))
                cache = new(chapter, new());
            foreach (Sheets.QuestRedo.QuestRedoParamStruct quest in redo.QuestRedoParam)
            {
                if (quest.Quest.RowId != 0)
                    cache.Quests.Add(quest.Quest.Value);
            }
            Chapters[chapter] = cache;
        }

        watch.Stop();
        return watch;
    }

    public RedoIndex GetChapter(ushort questId)
    {
        ReadOnlySeString name = (ReadOnlySeString)"";
        int index = -1;
        (QuestRedoChapterUI chapter, RedoCache cache) = Chapters.FirstOrDefault(entry => entry.Value.Quests.Any(q => (ushort)q.RowId == questId));
        if (cache != null)
        {
            name = cache.Chapter.ChapterName;
            index = cache.Quests.FindIndex(q => (ushort)q.RowId == questId);
            if (name.ByteLength == 0 || index == -1)
                return new(cache.Chapter, -1);
        }
        return new(chapter, index);
    }

    // If NG+ is already active, this silently overwrites the chapter index to 0 so NG+ is turned off.
    // Callers then need to run it again to send the right value. This is intended behaviour, do not change this. -alydev
    internal void SendCommand(int? chapterIndex = null, RedoChapter? redoChapter = null, QuestRedoChapterUI? questRedoChapter = null)
    {
        if (chapterIndex == null)
        {
            if (redoChapter != null)
                chapterIndex = (int)redoChapter;
            else if (questRedoChapter != null)
                chapterIndex = (int)questRedoChapter.Value.RowId;
        }
        if (IsActive())
            chapterIndex = 0;
        if (!IsLocked)
            GameMain.ExecuteCommand((int)GameCommand.QuestRedo, chapterIndex ?? 0);
        else
            _logger.LogWarning("Cowardly refusing to execute QuestRedo, UnlockQuest may not be complete");
    }

    internal bool IsActive() => QuestRedoHud != null && QuestRedoHud->IsAgentActive() && TryGetActiveChapter(out var _);

    internal bool TryGetActiveChapter(out QuestRedoChapterUI? chapter)
    {
        if (_gameGui.TryGetAddonByName<AtkUnitBase>("QuestRedoHud", out AtkUnitBase* addon) &&
                    addon->AtkValuesCount == 4 &&
                    // 0 seems to be active,
                    // 1 seems to be paused,
                    // 2 is unknown, but it happens e.g. before the quest 'Alzadaal's Legacy'
                    // 3 seems to be having /ng+ open while active,
                    // 4 seems to be when (a) suspending the chapter, or (b) having turned in a quest
                    addon->AtkValues[0].UInt is 0 or 2 or 3 or 4)
        {
            // redoHud+44 is chapter
            // redoHud+46 is quest
            ushort chapterId = MemoryHelper.Read<ushort>((nint)QuestRedoHud + 44);
            chapter = Chapters.Where(kvp => kvp.Key.RowId == chapterId).Select(kvp => kvp.Value.Chapter).FirstOrDefault();
            return true;
        }
        chapter = null;
        return false;
    }
}
