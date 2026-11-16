// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class EventInfoComponentTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompletedUnlock_KeepsFollowupFirst()
    {
        var quests = Pending([Event(434, 435, 5466)], complete: [434]);

        Assert.Equal([435, 5466], quests);
    }

    [Fact]
    public void FuturePrerequisites_StayInOrder()
    {
        Assert.Equal([434, 435, 5466], Pending([Event(434, 435, 5466)]));
    }

    [Fact]
    public void CompletedEvent_HasNoWork()
    {
        Assert.Empty(Pending([Event(434, 435, 5466)], complete: [434, 435, 5466]));
    }

    [Fact]
    public void AcceptedQuest_RemainsInQueue()
    {
        Assert.Equal([435], Pending([Event(435)], complete: [435], accepted: [435], unobtainable: [435]));
    }

    [Fact]
    public void UnobtainableQuest_IsOmitted()
    {
        Assert.Equal([435], Pending([Event(434, 435)], unobtainable: [434]));
    }

    [Fact]
    public void InactiveEvents_HaveNoWork()
    {
        Assert.Empty(Pending([
            Event(434) with { EndsAtUtc = Now.AddDays(-1) },
            Event(435) with { StartsAtUtc = Now.AddDays(1) }
        ]));
    }

    [Fact]
    public void SharedUnlock_IsOnlyQueuedOnce()
    {
        Assert.Equal([434, 435, 5466], Pending([Event(434, 435), Event(434, 5466)]));
    }

    private static EventInfoComponent.EventQuest Event(params ushort[] ids) =>
        new("Event", [.. ids.Select(x => (ElementId)new QuestId(x))], Now.AddDays(2), Now.AddDays(-2));

    private static ushort[] Pending(IEnumerable<EventInfoComponent.EventQuest> events,
        ushort[]? complete = null, ushort[]? accepted = null, ushort[]? unobtainable = null) =>
        [.. EventInfoComponent.GetActiveQuests(events, Now,
                x => accepted?.Contains(x.Value) == true,
                x => complete?.Contains(x.Value) == true,
                x => unobtainable?.Contains(x.Value) == true)
            .Select(x => x.Value)];
}
