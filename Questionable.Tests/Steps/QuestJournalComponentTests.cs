using Questionable.Model.Questing;
using Questionable.Windows.JournalComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestJournalComponentTests
{
    private static readonly ElementId Done = new QuestId(1);
    private static readonly ElementId Unobtainable = new QuestId(2);
    private static readonly ElementId Open = new QuestId(3);

    private static bool NothingLeft(params ElementId[] quests) =>
        QuestJournalComponent.NothingLeft(quests, x => x == Done, x => x == Unobtainable);

    [Fact]
    public void DoneAndUnobtainable_NothingLeft() => Assert.True(NothingLeft(Done, Unobtainable));

    [Fact]
    public void OnlyUnobtainable_NothingLeft() => Assert.True(NothingLeft(Unobtainable, Unobtainable));

    [Fact]
    public void OneOpenQuest_SomethingLeft() => Assert.False(NothingLeft(Done, Unobtainable, Open));
}
