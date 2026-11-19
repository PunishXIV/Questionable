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

    [SuppressMessage("Style", "IDE0074:Use compound assignment")]
    public static IReadOnlyDictionary<ElementId, QuestRoot> Quests
    {
        get
        {
            if (_quests == null)
            {
                _quests = [];
#if RELEASE
                //LoadQuests();
#endif
            }

            return _quests ?? throw new InvalidOperationException("quest data is not initialized");
        }
    }

    public static Stream QuestSchemaStream =>
        typeof(AssemblyQuestLoader).Assembly.GetManifestResourceStream("Questionable.QuestPaths.QuestSchema")!;

    private static void AddQuest(ElementId questId, QuestRoot root) => _quests![questId] = root;
}
