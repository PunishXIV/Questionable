using NSubstitute;
using Questionable.Domain;
using Questionable.Model.Questing;

namespace Questionable.Tests.TestData;

internal static class QuestTestData
{
    public static Quest CreateQuest(ElementId id, params QuestSequence[] sequences) =>
        new()
        {
            Id = id,
            Source = Quest.ESource.Assembly,
            Root = new QuestRoot { QuestSequence = [.. sequences] },
            Info = CreateQuestInfo(id),
        };

    public static IQuestInfo CreateQuestInfo(ElementId id)
    {
        var info = Substitute.For<IQuestInfo>();
        info.QuestId.Returns(id);
        return info;
    }

    public static QuestSequence Seq(byte sequence, params QuestStep[] steps) =>
        new() { Sequence = sequence, Steps = [.. steps] };

    /// <summary>
    /// Builds a quest graph the same way runtime does: sequence lives on the quest, step comes from that sequence.
    /// </summary>
    public static (Quest Quest, QuestSequence Sequence, QuestStep Step) FactoryContext(
        ElementId id,
        byte sequenceNumber,
        QuestStep step)
    {
        Quest quest = CreateQuest(id, Seq(sequenceNumber, step));
        QuestSequence sequence = quest.FindSequence(sequenceNumber)!;
        return (quest, sequence, sequence.Steps[0]);
    }
}
