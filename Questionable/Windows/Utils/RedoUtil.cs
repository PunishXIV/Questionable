using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ECommons;
using Sheets = Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
namespace Questionable.Windows.Utils;

internal sealed class RedoUtil
{
    internal readonly Dictionary<Sheets.QuestRedoChapter, RedoCache> RedoData = [];

    public RedoUtil()
    {
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
            if (!RedoData.TryGetValue(redo.Chapter.Value, out RedoCache? cache))
                cache = new(chapterUi.GetRow(redo.Chapter.RowId), new());
            foreach (Sheets.QuestRedo.QuestRedoParamStruct quest in redo.QuestRedoParam)
            {
                if (quest.Quest.RowId != 0)
                    cache.Quests.Add(quest.Quest.Value);
            }
            RedoData[redo.Chapter.Value] = cache;
        }

        watch.Stop();
        return watch;
    }

    /// <summary>
    /// Given a quest ID, returns a RedoIndex object containing either a valid chapter name and index of the quest
    /// within that chapter, or an empty ReadOnlySeString and the index -1. Failure: Index.Equals(-1)
    /// </summary>
    /// <param name="questId"></param>
    /// <returns></returns>
    public RedoIndex GetChapter(uint questId)
    {
        ReadOnlySeString name = (ReadOnlySeString)"";
        int index = -1;
        if (questId < 65536)
            questId += 65536;
        KeyValuePair<Sheets.QuestRedoChapter, RedoCache> result = RedoData.FirstOrDefault(entry => entry.Value.Quests.Any(q => q.RowId == questId));
        if (result.Value != null)
        {
            if (result.Value.ChapterUi != null)
            {
                name = result.Value.ChapterUi.Value.ChapterName;
                index = result.Value.Quests.FindIndex(q => q.RowId == questId);
            }
            if (name.ByteLength == 0 || index == -1)
                return new((ReadOnlySeString)"", -1);
        }
        return new(name, index);
    }
}

internal sealed record RedoCache(Sheets.QuestRedoChapterUI? ChapterUi, List<Sheets.Quest> Quests)
{
    public Sheets.QuestRedoChapterUI? ChapterUi = ChapterUi;
    public List<Sheets.Quest> Quests = Quests;
}

internal sealed record RedoIndex(ReadOnlySeString Name, int Index)
{
    public ReadOnlySeString Name = Name;
    public int Index = Index;

    public override string ToString() => $"{Name} (#{Index + 1})";
}