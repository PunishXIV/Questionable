using System.Diagnostics;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Sheets = Lumina.Excel.Sheets;
namespace Questionable.Windows.Utils;

internal unsafe sealed class RedoUtil
{
    internal readonly Dictionary<Sheets.QuestRedoChapterUI, RedoCache> RedoData = [];
    internal readonly AgentInterface* QuestRedoHud;
    private readonly IGameGuiAdapter _gameGui;

    public RedoUtil(IGameGuiAdapter gameGui)
    {
        _gameGui = gameGui;
        QuestRedoHud = AgentModule.Instance()->GetAgentByInternalId(AgentId.QuestRedoHud);
        RedoData = [];
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
            if (!RedoData.TryGetValue(chapter, out RedoCache? cache))
                cache = new(chapter, new());
            foreach (Sheets.QuestRedo.QuestRedoParamStruct quest in redo.QuestRedoParam)
            {
                if (quest.Quest.RowId != 0)
                    cache.Quests.Add(quest.Quest.Value);
            }
            RedoData[chapter] = cache;
        }

        watch.Stop();
        return watch;
    }

    public RedoIndex GetChapter(ushort questId)
    {
        ReadOnlySeString name = (ReadOnlySeString)"";
        int index = -1;
        (QuestRedoChapterUI key, RedoCache value) = RedoData.FirstOrDefault(entry => entry.Value.Quests.Any(q => (ushort)q.RowId == questId));
        if (value != null)
        {
            name = value.ChapterUi.ChapterName;
            index = value.Quests.FindIndex(q => (ushort)q.RowId == questId);
            if (name.ByteLength == 0 || index == -1)
                return new(value.ChapterUi, -1);
        }
        return new(key, index);
    }


    /// <summary>
    /// if NG+ is already active, this silently overwrites the chapter index to 0 so NG+ is turned off.
    /// users of this function then need to run it again to send the right value.
    /// this is intended behaviour. do not change this. -alydev
    /// </summary>
    /// <param name="chapterIndex"></param>
    /// <param name="redoChapter"></param>
    /// <param name="questRedoChapter"></param>
    internal void SendRedoCommand(int? chapterIndex = null, RedoChapter? redoChapter = null, QuestRedoChapterUI? questRedoChapter = null)
    {
        if (chapterIndex == null)
        {
            if (redoChapter != null)
                chapterIndex = (int)redoChapter;
            else if (questRedoChapter != null)
                chapterIndex = (int)questRedoChapter.Value.RowId;
        }
        if (IsRedoActive())
            chapterIndex = 0;
        GameMain.ExecuteCommand((int)GameCommand.QuestRedo, chapterIndex ?? 0);
    }

    internal bool IsRedoActive() => QuestRedoHud != null && QuestRedoHud->IsAgentActive() && TryGetActiveRedoChapter(out var _);

    internal bool TryGetActiveRedoChapter(out QuestRedoChapterUI? questRedoChapter)
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
            ushort chapter = MemoryHelper.Read<ushort>((nint)QuestRedoHud + 44);
            questRedoChapter = RedoData.Where(kvp => kvp.Key.RowId == chapter).Select(kvp => kvp.Value.ChapterUi).FirstOrDefault();
            return true;
        }
        questRedoChapter = null;
        return false;
    }
}
