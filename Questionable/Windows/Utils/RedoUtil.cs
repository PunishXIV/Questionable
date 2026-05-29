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
    private readonly Dictionary<uint, List<uint>> Dict;
    private Stopwatch Last { get; set; }

    public RedoUtil()
    {
        Dict = [];
        Last = Generate();
    }

    private Stopwatch Generate()
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
    public RedoIndex GetChapter(uint questId)
    {
        if (questId < 65536)
            questId += 65536;
        KeyValuePair<uint, List<uint>> result = Dict.FirstOrDefault(entry => entry.Value.Contains(questId));
        if (result.Value == null)
            return new((ReadOnlySeString)"", -1);
        ReadOnlySeString name = GenericHelpers.GetSheet<QuestRedoChapterUI>().GetRow(result.Key).ChapterName;
        int index = result.Value.IndexOf(questId);
        if (name.ByteLength == 0)
            return new((ReadOnlySeString)"", -1);
        return new(name, index);
    }
}

internal sealed record RedoIndex(ReadOnlySeString Name, int Index)
{
    public ReadOnlySeString Name = Name;
    public int Index = Index;

    public override string ToString() => $"{Name} (#{Index + 1})";
}