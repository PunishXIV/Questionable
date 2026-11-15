using System.Numerics;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Validation;
using Xunit;

using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Validation;

public sealed class WalkPointsTests
{
    private static QuestStep Walk(float x, uint territory = 1) =>
        new(EInteractionType.WalkTo, null, new Vector3(x, 0, 0), territory);

    private static QuestStep Talk(float x, uint territory = 1) =>
        new(EInteractionType.Interact, 1000, new Vector3(x, 0, 0), territory);

    private static List<WalkPoints.Spot> Find(params QuestSequence[] sequences) =>
        WalkPoints.Find(CreateQuest(new QuestId(1), sequences)).ToList();

    private static WalkPoints.Spot Spot(QuestStep from, QuestStep point, QuestStep to) =>
        new(CreateQuest(new QuestId(1)), WalkPoints.Kind.WalkPoint, new WalkPoints.StepRef(0, 1, point), new WalkPoints.StepRef(0, 0, from),
            new WalkPoints.StepRef(0, 2, to));

    [Fact]
    public void PlainWalkPoint_IsFound()
    {
        WalkPoints.Spot spot = Assert.Single(Find(Seq(0, Talk(0), Walk(50), Talk(100))));

        Assert.Equal(WalkPoints.Kind.WalkPoint, spot.Kind);
        Assert.Equal(1, spot.Point.Index);
        Assert.Equal(0, spot.From!.Index);
        Assert.Equal(2, spot.To!.Index);
    }

    [Theory]
    [InlineData("Avoids aggro")]
    [InlineData("navmesh gap")]
    public void SteeringComment_IsSkipped(string comment)
    {
        QuestStep walk = Walk(50);
        walk.Comment = comment;

        Assert.Empty(Find(Seq(0, Talk(0), walk, Talk(100))));
    }

    [Fact]
    public void CloseToNextStep_IsSkipped() =>
        Assert.Empty(Find(Seq(0, Talk(0), Walk(90), Talk(100))));

    [Fact]
    public void ZoneExit_IsSkipped() =>
        Assert.Empty(Find(Seq(0, Talk(0), Walk(50), Talk(100, territory: 2))));

    [Fact]
    public void Teleport_IsSkipped()
    {
        QuestStep walk = Walk(50);
        walk.AetheryteShortcut = EAetheryteLocation.Limsa;

        Assert.Empty(Find(Seq(0, Talk(0), walk, Talk(100))));
    }

    [Fact]
    public void Chain_SharesStartAndEnd()
    {
        List<WalkPoints.Spot> spots = Find(Seq(0, Talk(0), Walk(30), Walk(60), Talk(100)));

        Assert.Equal(2, spots.Count);
        Assert.All(spots, x => Assert.Equal((0, 3), (x.From!.Index, x.To!.Index)));
    }

    [Fact]
    public void StartInOtherZone_HasNoStart()
    {
        WalkPoints.Spot spot = Assert.Single(Find(Seq(0, Talk(0, territory: 2), Walk(50), Talk(100))));

        Assert.Null(spot.From);
    }

    [Fact]
    public void StartFromPreviousSequence_IsUsed()
    {
        WalkPoints.Spot spot = Assert.Single(Find(Seq(0, Talk(0)), Seq(1, Walk(50), Talk(100))));

        Assert.Equal((0, 0), (spot.From!.Sequence, spot.From.Index));
    }

    [Fact]
    public void NoNavmeshInteract_IsFound()
    {
        QuestStep talk = Talk(100);
        talk.DisableNavmesh = true;

        WalkPoints.Spot spot = Assert.Single(Find(Seq(0, Talk(0), talk)));

        Assert.Equal(WalkPoints.Kind.NoNavmesh, spot.Kind);
        Assert.Equal(0, spot.From!.Index);
    }

    [Fact]
    public void DirectPastPoint_IsSame()
    {
        WalkPoints.Spot spot = Spot(Talk(0), Walk(50), Talk(100));

        Assert.Equal(WalkPoints.Verdict.Same, WalkPoints.JudgeWalkPoint(spot, [new(50, 0, 3), new(100, 0, 0)]));
    }

    [Fact]
    public void DirectAwayFromPoint_IsOther()
    {
        WalkPoints.Spot spot = Spot(Talk(0), Walk(50), Talk(100));

        Assert.Equal(WalkPoints.Verdict.Other, WalkPoints.JudgeWalkPoint(spot, [new(50, 0, 30), new(100, 0, 0)]));
    }

    [Fact]
    public void DirectShortOfTarget_IsNoRoute()
    {
        WalkPoints.Spot spot = Spot(Talk(0), Walk(50), Talk(100));

        Assert.Equal(WalkPoints.Verdict.NoRoute, WalkPoints.JudgeWalkPoint(spot, [new(50, 0, 0), new(90, 0, 0)]));
        Assert.Equal(WalkPoints.Verdict.NoRoute, WalkPoints.JudgeWalkPoint(spot, []));
    }

    [Fact]
    public void FlyingPoint_AllowsWiderGap()
    {
        QuestStep walk = Walk(50);
        walk.Fly = true;
        WalkPoints.Spot spot = Spot(Talk(0), walk, Talk(100));

        Assert.Equal(WalkPoints.Verdict.Same, WalkPoints.JudgeWalkPoint(spot, [new(50, 8, 0), new(100, 0, 0)]));
    }

    [Fact]
    public void GroundOnlyPoint_IgnoresFly()
    {
        QuestStep walk = Walk(50);
        walk.Fly = true;
        walk.SkipConditions = new SkipConditions { StepIf = new SkipStepConditions { Flying = ELockedSkipCondition.Unlocked } };
        WalkPoints.Spot spot = Spot(Talk(0), walk, Talk(100));

        Assert.False(spot.FlyPoint);
        Assert.Equal(WalkPoints.Verdict.Other, WalkPoints.JudgeWalkPoint(spot, [new(50, 8, 0), new(100, 0, 0)]));
    }

    [Fact]
    public void NoNavmeshShortPath_IsReachable()
    {
        WalkPoints.Spot spot = new(CreateQuest(new QuestId(1)), WalkPoints.Kind.NoNavmesh, new WalkPoints.StepRef(0, 1, Talk(100)),
            new WalkPoints.StepRef(0, 0, Talk(0)), null);

        Assert.Equal(WalkPoints.Verdict.Reachable, WalkPoints.JudgeNoNavmesh(spot, [new(50, 0, 5), new(100, 0, 0)]));
        Assert.Equal(WalkPoints.Verdict.Detour, WalkPoints.JudgeNoNavmesh(spot, [new(50, 0, 60), new(100, 0, 0)]));
    }

    [Fact]
    public void Gap_MeasuresToSegment() =>
        Assert.Equal(3, WalkPoints.Gap(new Vector3(5, 3, 0), Vector3.Zero, [new(10, 0, 0)]), 3);
}
