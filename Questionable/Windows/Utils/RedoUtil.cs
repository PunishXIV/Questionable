using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Memory;
using ECommons;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Questionable.Data;
using Questionable.Utils;
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

    public RedoIndex GetChapter(uint questId)
    {
        ReadOnlySeString name = (ReadOnlySeString)"";
        int index = -1;
        if (questId < 65536)
            questId += 65536;
        (QuestRedoChapterUI key, RedoCache value) = RedoData.FirstOrDefault(entry => entry.Value.Quests.Any(q => q.RowId == questId));
        if (value != null)
        {
            name = value.ChapterUi.ChapterName;
            index = value.Quests.FindIndex(q => q.RowId == questId);
            if (name.ByteLength == 0 || index == -1)
                return new(value.ChapterUi, -1);
        }
        return new(key, index);
    }

    internal static void SendRedoCommand(QuestRedoChapterUI chapter) => SendRedoCommand((int)chapter.RowId);

    internal static void SendRedoCommand(int chapterIndex)
    {
        GameMain.ExecuteCommand((int)GameCommand.QuestRedo, chapterIndex);
    }

    internal bool IsRedoActive() => QuestRedoHud != null && QuestRedoHud->IsAgentActive();

    internal QuestRedoChapterUI? GetActiveRedoChapter()
    {
        if (IsRedoActive() && _gameGui.TryGetAddonByName<AtkUnitBase>("QuestRedoHud", out AtkUnitBase* addon) &&
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
            return RedoData.Where(kvp => kvp.Key.RowId == chapter).Select(kvp => kvp.Value.ChapterUi).FirstOrDefault();
        }
        return null;
    }
}

internal sealed record RedoCache(Sheets.QuestRedoChapterUI ChapterUi, List<Sheets.Quest> Quests)
{
    public Sheets.QuestRedoChapterUI ChapterUi = ChapterUi;
    public List<Sheets.Quest> Quests = Quests;
}

internal sealed record RedoIndex(QuestRedoChapterUI Chapter, int Index)
{
    public QuestRedoChapterUI Chapter = Chapter;
    public int Index = Index;

    public override string ToString()
    {
        int index = Index + 1;
        if (Chapter.RowId.Equals(1)) // ARR part 1
        {
            // handling for citystate starts numbering
            if (index.InRange(22,43)) // gridania
                index -= 21;
            if (index.InRange(43,65)) // uldah
                index -= 42;
            if (index == 65) // call of the sea limsa/gridania
                index = 22;
            if (index == 66) // call of the sea uldah
                index = 23;
        }
        return $"{Chapter.ChapterName} (#{index})";
    }
}