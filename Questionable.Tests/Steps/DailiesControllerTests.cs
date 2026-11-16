// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Questionable.Controller;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DailiesControllerTests
{
    private static readonly Dictionary<EAlliedSociety, (QuestId Id, EAlliedSocietyRank Rank)[]> Dailies = new()
    {
        [EAlliedSociety.Ixal] = [(new(1495), EAlliedSocietyRank.Neutral), (new(1503), EAlliedSocietyRank.Friendly), (new(1521), EAlliedSocietyRank.Allied)],
        [EAlliedSociety.Kobolds] = [(new(1325), EAlliedSocietyRank.Neutral), (new(1334), EAlliedSocietyRank.Recognized)],
    };

    private static List<QuestId> Plan(EAlliedSociety[] societies, int allowances, Func<QuestId, bool>? canAccept = null) =>
        DailiesController.Plan(societies, x => Dailies.GetValueOrDefault(x, []), canAccept ?? (_ => true), allowances);

    [Fact]
    public void Plan_ListOrderThenRank()
    {
        Assert.Equal([new(1334), new(1325), new(1521), new(1503), new(1495)],
            Plan([EAlliedSociety.Kobolds, EAlliedSociety.Ixal], 12));
    }

    [Fact]
    public void Plan_StopsAtAllowances()
    {
        Assert.Equal([new(1521), new(1503)], Plan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds], 2));
        Assert.Empty(Plan([EAlliedSociety.Ixal], 0));
    }

    [Fact]
    public void Plan_SkipsUnacceptable()
    {
        Assert.Equal([new(1503), new(1495)],
            Plan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds], 2, x => x.Value is not 1521));
    }

    [Fact]
    public void Plan_ReorderingChangesAllocation()
    {
        Assert.Equal([new(1521), new(1503), new(1495), new(1334)],
            Plan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds], 4));
        Assert.Equal([new(1334), new(1325), new(1521), new(1503)],
            Plan([EAlliedSociety.Kobolds, EAlliedSociety.Ixal], 4));
    }

    [Fact]
    public void Plan_UnavailableSocietyIsSkipped() =>
        Assert.Equal([new(1334), new(1325)],
            Plan([EAlliedSociety.Ananta, EAlliedSociety.Kobolds], 2));

    [Fact]
    public void Plan_IneligibleSocietyIsSkipped() =>
        Assert.Equal([new(1334), new(1325)],
            Plan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds], 2, x => x.Value < 1400));

    [Fact]
    public void Plan_EmptySelectionAcceptsNothing() => Assert.Empty(Plan([], 12));

    [Fact]
    public void Plan_NegativeAllowancesAcceptsNothing() =>
        Assert.Empty(Plan([EAlliedSociety.Ixal], -1));

    [Fact]
    public void Plan_DuplicateSocietyCountsOnce() =>
        Assert.Equal([new(1521), new(1503), new(1495), new(1334)],
            Plan([EAlliedSociety.Ixal, EAlliedSociety.Ixal, EAlliedSociety.Kobolds], 4));

    [Fact]
    public void Plan_DuplicateQuestCountsOnce()
    {
        var quests = new (QuestId, EAlliedSocietyRank)[]
        {
            (new(1495), EAlliedSocietyRank.Neutral),
            (new(1495), EAlliedSocietyRank.Neutral),
            (new(1503), EAlliedSocietyRank.Friendly),
        };
        Assert.Equal([new(1503), new(1495)],
            DailiesController.Plan([EAlliedSociety.Ixal], _ => quests, _ => true, 3));
    }
}
