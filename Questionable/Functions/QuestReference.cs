using Questionable.Model.Questing;

namespace Questionable.Functions;

public sealed record QuestReference(ElementId? CurrentQuest, byte Sequence, MainScenarioQuestState State)
{
    public static QuestReference NoQuest(MainScenarioQuestState state) => new(CurrentQuest: null, 0, state);
}
