// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Numerics;
using System.Text.Json;
using Dalamud.Plugin.Services;
using NSubstitute;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DailiesSafetyTests
{
    private readonly FakeQuestGameState _state = new();
    private readonly FakeQuestCatalog _catalog = new();
    private readonly QuestFunctions _functions;

    public DailiesSafetyTests() => _functions = new(_catalog, _state, new AlliedSocietyData(),
        new Configuration(), Substitute.For<IChatGui>());

    private Quest Load(ushort id, EAlliedSociety society, bool repeatable = false)
    {
        string resource = typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
            .Single(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) &&
                x.Contains($"/{id}_", StringComparison.Ordinal) && x.EndsWith(".json", StringComparison.Ordinal));
        using Stream stream = new EmbeddedQuest(resource).OpenStream();
        QuestRoot root = JsonSerializer.Deserialize<QuestRoot>(stream)!;
        Quest quest = _catalog.Add(TestQuestInfo.Create(id).With(nameof(QuestInfo.AlliedSociety), society)
                .With(nameof(QuestInfo.IsRepeatable), repeatable),
            [.. root.QuestSequence]);
        quest.Root.Disabled = root.Disabled;
        quest.Root.Interruptible = root.Interruptible;
        return quest;
    }

    private DailiesPlan.Entry Entry(Quest quest, byte sequence, bool daily = true)
    {
        QuestStep? target = quest.FindSequence(sequence)!.Steps.FirstOrDefault(x => x.Position != null);
        return new((QuestId)quest.Id, quest.Info.AlliedSociety, daily, true, false,
            EAlliedSocietyRank.Neutral, sequence, target?.TerritoryId ?? 0, target?.Position,
            KeepCurrent: _functions.KeepDaily(quest, sequence));
    }

    [Fact]
    public void Sanuwa_KeepsQuestBeforeNearbyDaily()
    {
        Quest mounted = Load(2173, EAlliedSociety.VanuVanu);
        Quest nearby = Load(2185, EAlliedSociety.VanuVanu);
        _state.Accepted.UnionWith([2173, 2185]);
        _state.CurrentMountId = 66;
        Vector3 position = mounted.FindSequence(1)!.Steps.Last().Position!.Value;
        var plan = new DailiesPlan([EAlliedSociety.VanuVanu]);
        var entries = new[] { Entry(mounted, 2), Entry(nearby, 1) };

        Assert.True(Vector3.DistanceSquared(position, entries[1].Position!.Value) <
            Vector3.DistanceSquared(position, entries[0].Position!.Value));
        Assert.Equal(mounted.Id, plan.Choose(entries, 0, 401, position, mounted.Id)!.Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void Sanuwa_KeepsQuestBeforeRankUp(byte sequence)
    {
        Quest mounted = Load(2173, EAlliedSociety.VanuVanu);
        Quest story = Load(2165, EAlliedSociety.VanuVanu);
        _state.Accepted.Add(2173);
        _state.CurrentMountId = 66;
        var plan = new DailiesPlan([EAlliedSociety.VanuVanu]);
        DailiesPlan.Entry rankUp = Entry(story, 0, daily: false) with { Accepted = false, Ready = true };

        Assert.Equal(mounted.Id, plan.Choose([Entry(mounted, sequence), rankUp], 12, 401,
            Vector3.Zero, mounted.Id)!.Id);
        plan.Complete(mounted.Id);
        Assert.Equal(story.Id, plan.Choose([Entry(mounted, sequence), rankUp], 12, 401,
            Vector3.Zero, mounted.Id)!.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    public void DeckTheHut_KeepsUntilCompletion(byte sequence)
    {
        Quest quest = Load(1257, EAlliedSociety.Sylphs);
        _state.Accepted.Add(1257);

        Assert.False(quest.Root.Interruptible);
        Assert.True(_functions.KeepDaily(quest, sequence));
        _state.Accepted.Clear();
        Assert.False(_functions.KeepDaily(quest, sequence));
    }

    [Fact]
    public void SylphMount_HoldsAfterIssuer()
    {
        Quest quest = Load(1271, EAlliedSociety.Sylphs);
        _state.Accepted.Add(1271);
        _state.CurrentMountId = 24;
        Assert.True(_functions.KeepDaily(quest, 2));
    }

    [Fact]
    public void Mount_UnrelatedOrLostDoesNotHold()
    {
        Quest quest = Load(2173, EAlliedSociety.VanuVanu);
        Quest nearby = Load(2185, EAlliedSociety.VanuVanu);
        _state.Accepted.UnionWith([2173, 2185]);
        _state.CurrentMountId = 66;
        Assert.False(_functions.KeepDaily(nearby, 1));
        Assert.False(_functions.KeepDaily(quest, 1));
        _state.CurrentMountId = 23;
        Assert.False(_functions.KeepDaily(quest, 2));
        _state.CurrentMountId = 0;
        Assert.False(_functions.KeepDaily(quest, 2));
        _state.CurrentMountId = 66;
        _state.Accepted.Clear();
        Assert.False(_functions.KeepDaily(quest, 2));
    }

    [Fact]
    public void DisabledFate_IsNotAutomatic()
    {
        Quest quest = Load(1336, EAlliedSociety.Kobolds);
        Assert.True(quest.Root.Disabled);
        Assert.Equal(EInteractionType.Instruction, quest.FindSequence(0)!.Steps[0].InteractionType);
        Assert.False(_functions.CanQueueSociety(quest.Id));
        Assert.Contains("disabled", DailiesController.GetBlockReason(quest, (QuestInfo)quest.Info, false, true)!, System.StringComparison.Ordinal);
        Assert.NotNull(quest.FindSequence(0));
    }

    [Fact]
    public void DisabledFate_TurnInOnceDone()
    {
        Quest quest = Load(1336, EAlliedSociety.Kobolds, repeatable: true);
        Assert.False(_functions.OnlyTurnInLeft(quest, 255));
        _state.Accepted.Add(1336);
        Assert.False(_functions.OnlyTurnInLeft(quest, 255));
        _state.ReadyToTurnIn.Add(quest.Info.Name);
        Assert.True(_functions.OnlyTurnInLeft(quest, 255));
        Assert.False(_functions.OnlyTurnInLeft(quest, 0));
    }

    [Fact]
    public void ManualDaily_IsNotAutomatic()
    {
        Quest quest = _catalog.Add(TestQuestInfo.Create(1516).With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
                .With(nameof(QuestInfo.IsRepeatable), true),
            [QuestTestData.Seq(1, new QuestStep { InteractionType = EInteractionType.Instruction })]);
        Assert.False(_functions.CanQueueSociety(quest.Id));
        Assert.Contains("can't do on its own",
            DailiesController.GetBlockReason(quest, (QuestInfo)quest.Info, false, true, needsPlayer: true)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SnipeDaily_NeedsAutoSnipe(bool autoSnipe, bool needsPlayer) =>
        Assert.Equal(needsPlayer, QuestFunctions.NeedsPlayer(Load(2991, EAlliedSociety.Kojin, repeatable: true), autoSnipe));

    [Fact]
    public void ManualStory_NotSkipped() =>
        Assert.False(QuestFunctions.NeedsPlayer(Load(2974, EAlliedSociety.Kojin), autoSnipe: false));

    [Fact]
    public void Disabled_DoesNotHoldCurrent()
    {
        var plan = new DailiesPlan([EAlliedSociety.Kobolds]);
        var blocked = new DailiesPlan.Entry(new(1336), EAlliedSociety.Kobolds, true, true, false,
            EAlliedSocietyRank.Neutral, 1, 180, Vector3.Zero, BlockReason: "disabled", KeepCurrent: true);

        Assert.Null(plan.Choose([blocked], 0, 180, Vector3.Zero, blocked.Id));
        Assert.Equal(1, plan.Unfinished);
        Assert.Single(plan.Skipped);
    }

    [Fact]
    public void OfferedLockedDaily_ExplainsPrerequisite()
    {
        QuestId id = new(1495);
        _catalog.Add(TestQuestInfo.Create(1495, previous: [new(new QuestId(1000))])
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
            .With(nameof(QuestInfo.IsRepeatable), true));
        _catalog.Add(TestQuestInfo.Create(1000, "Required quest"));
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Ixal] = [id];

        Assert.Contains("Required quest", _functions.GetDailyBlock(id)!, System.StringComparison.Ordinal);
        var plan = new DailiesPlan([EAlliedSociety.Ixal]);
        var blocked = new DailiesPlan.Entry(id, EAlliedSociety.Ixal, true, false, false,
            EAlliedSocietyRank.Neutral, 0, 154, Vector3.Zero, BlockReason: _functions.GetDailyBlock(id));
        Assert.Null(plan.Choose([blocked], 12, 154, Vector3.Zero));
        Assert.Single(plan.Blocked);
        Assert.Equal(0, plan.Unfinished);
        _state.Complete.Add(1000);
        Assert.Null(_functions.GetDailyBlock(id));
    }

    [Fact]
    public void OfferedLockedDaily_ExplainsAetheryte()
    {
        QuestId id = new(1495);
        _catalog.Add(TestQuestInfo.Create(1495)
                .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
                .With(nameof(QuestInfo.IsRepeatable), true),
            QuestTestData.Seq(0, new QuestStep { TerritoryId = 155 }));
        _state.Aetherytes[155] = [EAetheryteLocation.CoerthasCentralHighlandsCampDragonhead];
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Ixal] = [id];
        _state.LockedAetherytes.Add(EAetheryteLocation.CoerthasCentralHighlandsCampDragonhead);

        Assert.Contains("Aetheryte", _functions.GetDailyBlock(id)!, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not offered")]
    [InlineData("completed today")]
    [InlineData("no allowances")]
    [InlineData("accepted")]
    public void DailyBlock_IgnoresNormalNoWork(string state)
    {
        QuestId id = new(1495);
        _catalog.Add(TestQuestInfo.Create(1495, previous: [new(new QuestId(1000))])
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
            .With(nameof(QuestInfo.IsRepeatable), true));
        _catalog.Add(TestQuestInfo.Create(1000, "Required quest"));
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Ixal] = [id];
        switch (state)
        {
            case "not offered": _state.AvailableAlliedSocietyQuests.Clear(); break;
            case "completed today": _state.DailyCompleted.Add(id.Value); break;
            case "no allowances": _state.SocietyAllowances = 0; break;
            case "accepted": _state.Accepted.Add(id.Value); break;
        }

        Assert.Null(_functions.GetDailyBlock(id));
    }

    [Fact]
    public void RemovedQuest_IsUnobtainable() => Assert.True(_functions.IsQuestUnobtainable(new QuestId(1006)));

    [Fact]
    public void MissingPath_OfferedDailyStaysVisible()
    {
        QuestId id = new(1495);
        QuestInfo info = TestQuestInfo.Create(id.Value)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
            .With(nameof(QuestInfo.IsRepeatable), true);
        IQuestCatalog catalog = Substitute.For<IQuestCatalog>();
        catalog.GetQuestInfo(id).Returns(info);
        catalog.TryGetQuestInfo(id, out Arg.Any<IQuestInfo?>()).Returns(call =>
        {
            call[1] = info;
            return true;
        });
        catalog.GetLockedClassQuests().Returns([]);
        var functions = new QuestFunctions(catalog, _state, new AlliedSocietyData(),
            new Configuration(), Substitute.For<IChatGui>());
        _state.Complete.Add(id.Value);
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Ixal] = [id];

        Assert.True(functions.IsReadyToAcceptQuest(id, ignoreLevel: true));
        Assert.False(functions.CanQueueSociety(id));
        _state.DailyCompleted.Add(id.Value);
        Assert.False(functions.IsReadyToAcceptQuest(id, ignoreLevel: true));
        _state.DailyCompleted.Clear();
        _state.SocietyAllowances = 0;
        Assert.False(functions.IsReadyToAcceptQuest(id, ignoreLevel: true));
        _state.SocietyAllowances = 12;
        _state.AvailableAlliedSocietyQuests.Clear();
        Assert.False(functions.IsReadyToAcceptQuest(id, ignoreLevel: true));
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Ixal] = [id];
        _state.Accepted.Add(id.Value);
        Assert.False(functions.IsReadyToAcceptQuest(id, ignoreLevel: true));
    }

    [Fact]
    public void BlockReason_NamesMissingPathAndLevel()
    {
        QuestInfo info = TestQuestInfo.Create(123, "Daily", level: 50)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal);
        Quest quest = _catalog.Add(info);
        Assert.Contains("No quest path", DailiesController.GetBlockReason(null, info, false, true)!, System.StringComparison.Ordinal);
        Assert.Contains("level 50", DailiesController.GetBlockReason(quest, info, false, false)!, System.StringComparison.Ordinal);
        Assert.Null(DailiesController.GetBlockReason(quest, info, true, false));
        Assert.Null(DailiesController.GetBlockReason(quest, info, false, true));
    }

    [Fact]
    public void UnlockReason_NamesQuestAndMissing()
    {
        string reason = DailiesController.UnlockReason("Peace for Thanalan", ["Prev: Into the Sands"]);
        Assert.Contains("Peace for Thanalan", reason, System.StringComparison.Ordinal);
        Assert.Contains("Into the Sands", reason, System.StringComparison.Ordinal);
        Assert.Contains("can't be accepted", DailiesController.UnlockReason("Peace for Thanalan", []), System.StringComparison.Ordinal);
    }

    private static (List<QuestId> Chain, QuestId? Story) Chain(ushort[] done, params QuestInfo[] quests) =>
        QuestChain.Before(new QuestId(10), x => quests.FirstOrDefault(y => y.QuestId.Equals(x)),
            x => done.Contains(x.QuestId.Value));

    [Fact]
    public void UnlockChain_RunsPrerequisitesFirst()
    {
        var (chain, story) = Chain([7],
            TestQuestInfo.Create(10, previous: new PreviousQuestInfo(new QuestId(9))),
            TestQuestInfo.Create(9, previous: [new PreviousQuestInfo(new QuestId(8)), new PreviousQuestInfo(new QuestId(7))]),
            TestQuestInfo.Create(8));
        Assert.Null(story);
        Assert.Equal([8, 9, 10], chain.Select(x => (int)x.Value));
    }

    [Fact]
    public void UnlockChain_StopsAtMainStory()
    {
        var (_, story) = Chain([],
            TestQuestInfo.Create(10, previous: new PreviousQuestInfo(new QuestId(9))),
            TestQuestInfo.Create(9, mainScenario: true));
        Assert.Equal(new QuestId(9), story);
    }

    [Fact]
    public void UnlockChain_AnyOnePicksSideQuest()
    {
        var (chain, story) = Chain([],
            TestQuestInfo.Create(10, previousJoin: EQuestJoin.AtLeastOne,
                previous: [new PreviousQuestInfo(new QuestId(9)), new PreviousQuestInfo(new QuestId(8))]),
            TestQuestInfo.Create(9, mainScenario: true),
            TestQuestInfo.Create(8));
        Assert.Null(story);
        Assert.Equal([8, 10], chain.Select(x => (int)x.Value));
    }
}
