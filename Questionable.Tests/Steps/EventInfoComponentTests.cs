// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Windows.QuestComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class EventInfoComponentTests
{
    [Fact]
    public void RunningFestival_ListsItsQuests()
    {
        var events = Events([(900, 0)], Seasonal(4796, 900), Seasonal(4797, 900), Seasonal(5466, 901));

        Assert.Equal([4796, 4797], Ids(Assert.Single(events)));
        Assert.Equal("Quest 4796", events[0].Name);
        Assert.Null(events[0].EndsAtUtc);
    }

    [Fact]
    public void KnownFestival_UsesDetails()
    {
        DateTime ends = new(2026, 12, 9, 14, 59, 0, DateTimeKind.Utc);
        var gameEvent = Assert.Single(EventInfoComponent.GetEvents([Seasonal(4796, 147)], [(147, 0)], _ => null, _ => true,
            x => x == 147 ? new EventInfoComponent.EventDetails("FFXVI", ends) : null));

        Assert.Equal("FFXVI", gameEvent.Name);
        Assert.Equal(ends, gameEvent.EndsAtUtc);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void PhaseRange_DecidesQuest(ushort phase, bool shown)
    {
        var events = Events([(5, phase)], Seasonal(100, 5, begin: 1, end: 3));

        Assert.Equal(shown, events.Count == 1);
    }

    [Fact]
    public void Prerequisites_ComeFirst()
    {
        var events = Events([(257, 0)], [434],
            Seasonal(5466, 257, previous: 435), TestQuestInfo.Create(435, previous: new PreviousQuestInfo(new QuestId(434))),
            TestQuestInfo.Create(434));

        Assert.Equal([435, 5466], Ids(Assert.Single(events)));
    }

    [Fact]
    public void SharedPrerequisite_ListedOnce()
    {
        var events = Events([(9, 0)], [],
            Seasonal(200, 9, previous: 150), Seasonal(201, 9, previous: 150), TestQuestInfo.Create(150));

        Assert.Equal([150, 200, 201], Ids(Assert.Single(events)));
    }

    [Fact]
    public void ActiveQuests_SkipDoneAndUnobtainable()
    {
        var events = Events([(9, 0)], Seasonal(1, 9), Seasonal(2, 9), Seasonal(3, 9), Seasonal(4, 9));

        ushort[] active = [.. EventInfoComponent.GetActiveQuests(events,
                x => x.Value == 4,
                x => x.Value is 2 or 4,
                x => x.Value is 3 or 4)
            .Select(x => x.Value)];

        Assert.Equal([1, 4], active);
    }

    private static QuestInfo Seasonal(ushort id, ushort festival, ushort begin = 0, ushort end = 1, ushort? previous = null) =>
        TestQuestInfo.Create(id, previous: previous is { } p ? [new PreviousQuestInfo(new QuestId(p))] : [])
            .With(nameof(QuestInfo.IsSeasonalEvent), true)
            .With(nameof(QuestInfo.FestivalId), festival)
            .With(nameof(QuestInfo.FestivalBegin), begin)
            .With(nameof(QuestInfo.FestivalEnd), end);

    private static List<EventInfoComponent.Event> Events((ushort, ushort)[] festivals, params QuestInfo[] quests) =>
        Events(festivals, [], quests);

    private static List<EventInfoComponent.Event> Events((ushort, ushort)[] festivals, ushort[] done, params QuestInfo[] quests) =>
        EventInfoComponent.GetEvents(quests.Where(x => x.IsSeasonalEvent), festivals,
            x => quests.FirstOrDefault(y => y.QuestId.Equals(x)),
            x => done.Contains(x.QuestId.Value), _ => null);

    private static int[] Ids(EventInfoComponent.Event gameEvent) => [.. gameEvent.QuestIds.Select(x => (int)x.Value)];
}
