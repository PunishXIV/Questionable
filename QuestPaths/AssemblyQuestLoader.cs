using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Questionable.Model.Questing;

namespace Questionable.QuestPaths;

[SuppressMessage("ReSharper", "PartialTypeWithSinglePart", Justification = "Required for RELEASE")]
public static partial class AssemblyQuestLoader
{
    private static Dictionary<ElementId, QuestRoot>? _quests;

    public static IReadOnlyDictionary<ElementId, QuestRoot> GetQuests()
    {
        if (_quests == null)
        {
            _quests = [];
#if RELEASE
            LoadQuests();
#endif
        }

        return _quests ?? throw new InvalidOperationException("quest data is not initialized");
    }

    public static Stream QuestSchema =>
        typeof(AssemblyQuestLoader).Assembly.GetManifestResourceStream("Questionable.QuestPaths.QuestSchema")!;

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private static void AddQuest(ElementId questId, QuestRoot root) => _quests![questId] = root;
}
