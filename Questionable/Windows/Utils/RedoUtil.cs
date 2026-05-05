using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ECommons;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
namespace Questionable.Windows.Utils;

internal sealed class RedoUtil
{
    public Dictionary<uint, List<uint>> Dict;

    public RedoUtil()
    {
        Dict = [];
        Last = Generate();
    }
    public Stopwatch Last { get; private set; }

    public Tuple<ReadOnlySeString, int> GetChapter(uint questId)
    {
        KeyValuePair<uint, List<uint>> result = Dict.FirstOrDefault(entry => entry.Value.Contains(questId));
        if (result.Value == null)
            return new((ReadOnlySeString)"", -1);
        int index = result.Value.IndexOf(questId);
        return new(GenericHelpers.GetSheet<QuestRedoChapterUI>().GetRow(result.Key).ChapterName, index);
    }

    public Stopwatch Generate()
    {
        Stopwatch watch = Stopwatch.StartNew();
        foreach (QuestRedo chapter in GenericHelpers.GetSheet<QuestRedo>())
        {
            if (chapter.Chapter.RowId == 0)
                continue;
            if (!Dict.ContainsKey(chapter.Chapter.RowId))
                Dict[chapter.Chapter.RowId] = [];
            foreach (QuestRedo.QuestRedoParamStruct quest in chapter.QuestRedoParam)
            {
                if (quest.Quest.RowId != 0)
                    Dict[chapter.Chapter.RowId].Add(quest.Quest.RowId);
            }
        }

        watch.Stop();
        return watch;
    }
}
