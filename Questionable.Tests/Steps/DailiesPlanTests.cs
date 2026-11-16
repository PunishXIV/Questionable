// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Numerics;
using Questionable.Controller;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DailiesPlanTests
{
    private static DailiesPlan.Entry Daily(ushort id, EAlliedSociety society = EAlliedSociety.Ixal,
        uint zone = 154, bool accepted = false, byte sequence = 1, float x = 0) =>
        new(new(id), society, true, accepted, !accepted, EAlliedSocietyRank.Neutral,
            accepted ? sequence : (byte)0, zone, new Vector3(x, 0, 0));

    [Fact]
    public void Travel_DoesNotChangeAllowances()
    {
        var entries = new[] { Daily(1, zone: 155), Daily(2, EAlliedSociety.Kobolds, zone: 180) };
        var plan = new DailiesPlan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds]);
        Assert.Equal(new QuestId(1), plan.Choose(entries, 1, 180, Vector3.Zero)!.Id);
        plan = new([EAlliedSociety.Ixal, EAlliedSociety.Kobolds]);
        Assert.Equal(new QuestId(2), plan.Choose(entries, 2, 180, Vector3.Zero)!.Id);
    }

    [Fact]
    public void Travel_NearestInCurrentZone()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        Assert.Equal(new QuestId(2), plan.Choose([Daily(1, x: 100), Daily(2, x: 5)], 2, 154, Vector3.Zero)!.Id);
    }

    [Fact]
    public void RankUp_PrecedesDailyAllowance()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var story = Daily(3, zone: 155) with { Daily = false };
        Assert.Equal(story, plan.Choose([Daily(1), story], 12, 154, Vector3.Zero));
        plan.Complete(story.Id);
        Assert.Equal(new QuestId(1), plan.Choose([Daily(1), story], 12, 154, Vector3.Zero)!.Id);
        Assert.Equal(1, plan.FinishedStories);
    }

    [Fact]
    public void RankUp_RefreshesNewRankDailies()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var story = Daily(3) with { Daily = false };
        plan.Choose([Daily(1), story], 1, 154, Vector3.Zero);
        plan.Complete(story.Id);
        var higher = Daily(2) with { Rank = EAlliedSocietyRank.Friendly };
        Assert.Equal(higher, plan.Choose([Daily(1), higher], 1, 154, Vector3.Zero));
    }

    [Fact]
    public void RankUp_AlsoRunsAtZeroAllowances()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var story = Daily(3) with { Daily = false };
        Assert.Equal(story, plan.Choose([Daily(1), story], 0, 154, Vector3.Zero));
    }

    [Fact]
    public void RankUp_RechecksAfterDailyTurnIn()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var first = Daily(1, accepted: true, sequence: 255);
        var next = Daily(2, accepted: true, sequence: 255);
        Assert.Equal(first, plan.Choose([first, next], 0, 154, Vector3.Zero));
        plan.Complete(first.Id);
        var story = Daily(3, zone: 155) with { Daily = false };
        Assert.Equal(story, plan.Choose([next, story], 0, 154, Vector3.Zero));
    }

    [Fact]
    public void Travel_OtherZoneCoordinatesIgnored()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal, EAlliedSociety.Kobolds]);
        var ixal = Daily(1, zone: 154, x: 500);
        var kobold = Daily(2, EAlliedSociety.Kobolds, zone: 180, x: 0);
        Assert.Equal(ixal, plan.Choose([kobold, ixal], 2, 999, Vector3.Zero));
    }

    [Fact]
    public void RankUp_UnsupportedStopsBeforeDaily()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var story = Daily(3) with { Daily = false, BlockReason = "Unsupported battle" };
        Assert.Null(plan.Choose([Daily(1), story], 12, 154, Vector3.Zero));
        Assert.Equal("Unsupported battle", plan.StopReason);
    }

    [Fact]
    public void Batch_AcceptsBeforeDoingQuests()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        Assert.Equal(new QuestId(1), plan.Choose([Daily(1), Daily(2, accepted: true)], 1, 154, Vector3.Zero)!.Id);
        Assert.Equal(new QuestId(2), plan.Choose([Daily(1, accepted: true, sequence: 255), Daily(2, accepted: true)], 0, 154, Vector3.Zero)!.Id);
    }

    [Fact]
    public void Batch_PicksMostStagesOutsideZone()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var entries = new[] { Daily(1, zone: 155, accepted: true), Daily(2, zone: 156, accepted: true), Daily(3, zone: 156, accepted: true) };
        Assert.Equal((uint)156, plan.Choose(entries, 0, 154, Vector3.Zero)!.Territory);
    }

    [Fact]
    public void Craft_TimedMaterialsGoFirst()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var craft = Daily(2, accepted: true, sequence: 255) with { CraftReady = true };
        Assert.Equal(craft, plan.Choose([Daily(1), craft], 12, 154, Vector3.Zero));
    }

    [Fact]
    public void Retry_OnceThenSkipsAcceptedQuest()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var entries = new[] { Daily(1, accepted: true), Daily(2, accepted: true) };
        plan.Choose(entries, 0, 154, Vector3.Zero);
        Assert.Equal(EDailiesFailure.Retry, plan.Fail(new QuestId(1), "busy"));
        Assert.Equal(new QuestId(1), plan.Choose(entries, 0, 154, Vector3.Zero)!.Id);
        Assert.Equal(EDailiesFailure.Skip, plan.Fail(new QuestId(1), "still busy"));
        Assert.Equal(new QuestId(2), plan.Choose(entries, 0, 154, Vector3.Zero)!.Id);
        Assert.Equal("still busy", plan.Skipped[new QuestId(1)]);
        plan.Complete(new QuestId(2));
        Assert.Null(plan.Choose(entries, 0, 154, Vector3.Zero));
        Assert.Equal(1, plan.FinishedDailies);
        Assert.Equal(1, plan.Unfinished);
    }

    [Fact]
    public void Retry_CountIsPerQuest()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        plan.Choose([Daily(1, accepted: true), Daily(2, accepted: true)], 0, 154, Vector3.Zero);
        Assert.Equal(EDailiesFailure.Retry, plan.Fail(new QuestId(1), "first"));
        Assert.Equal(EDailiesFailure.Retry, plan.Fail(new QuestId(2), "first"));
    }

    [Fact]
    public void RankUp_FailureStopsRun()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        plan.Choose([Daily(1) with { Daily = false }], 12, 154, Vector3.Zero);
        Assert.Equal(EDailiesFailure.Stop, plan.Fail(new QuestId(1), "battle stopped"));
        Assert.Equal("battle stopped", plan.StopReason);
    }

    [Fact]
    public void Selection_ExcludesOtherSocieties()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        Assert.Null(plan.Choose([Daily(1, EAlliedSociety.Kobolds, accepted: true)], 12, 154, Vector3.Zero));
    }

    [Fact]
    public void Completed_RepeatableIsNotSelected()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var daily = Daily(1, accepted: true);
        plan.Choose([daily], 0, 154, Vector3.Zero);
        plan.Complete(daily.Id);
        plan.Complete(daily.Id);
        Assert.Null(plan.Choose([daily], 12, 154, Vector3.Zero));
        Assert.Equal(1, plan.FinishedDailies);
    }

    [Fact]
    public void ExistingDailies_RunWithoutAllowance()
    {
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var accepted = Daily(1, accepted: true);
        Assert.Equal(accepted, plan.Choose([accepted, Daily(2)], 0, 154, Vector3.Zero));
    }
}
