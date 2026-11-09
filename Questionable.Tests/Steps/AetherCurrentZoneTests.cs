using System.Collections.Immutable;
using System.Globalization;
using Dalamud.Plugin.Services;
using NSubstitute;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AetherCurrentZoneTests
{
    private static readonly AetherCurrentId Zone = new(612);
    private readonly FakeQuestGameState _state = new();
    private readonly FakeQuestCatalog _catalog = new();
    private readonly QuestFunctions _functions;

    public AetherCurrentZoneTests()
    {
        _functions = new QuestFunctions(_catalog, _state, new AlliedSocietyData(), new Configuration(), Substitute.For<IChatGui>());
        _catalog.Add(TestQuestInfo.Create(1, "Early"));
        _catalog.Add(TestQuestInfo.Create(2, "Late"));
        _catalog.Add(new AetherCurrentZoneInfo(Zone, "Zone", EExpansionVersion.Stormblood, [10, 11],
            [new(new QuestId(1)), new(new QuestId(2))]), QuestTestData.Seq(0, new QuestStep()));
    }

    [Fact]
    public void Zone_DoneOnlyWhenAllAttuned()
    {
        _state.AetherCurrents.Add(10);
        Assert.False(_functions.IsQuestComplete(Zone));
        Assert.Equal([11u], _functions.GetMissingAetherCurrents(Zone));

        _state.AetherCurrents.Add(11);
        Assert.True(_functions.IsQuestComplete(Zone));
        Assert.Empty(_functions.GetMissingAetherCurrents(Zone));
    }

    [Fact]
    public void Zone_LockedUntilEveryQuestDone()
    {
        _state.Complete.Add(1);
        Assert.True(_functions.IsQuestLocked(Zone).isLocked);

        _state.Complete.Add(2);
        Assert.False(_functions.IsQuestLocked(Zone).isLocked);
    }

    [Fact]
    public void Zone_NeverAccepted() => Assert.False(_functions.IsQuestAccepted(Zone));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void SkipSetting_OnlyAppliesInsideQuests(bool inQuest, bool setting, bool skip)
    {
        ElementId owner = inQuest ? new QuestId(1) : Zone;
        QuestStep step = new() { InteractionType = EInteractionType.AttuneAetherCurrent };
        Assert.Equal(skip, SkipCondition.Executor.ShouldSkipForSetting(owner, step, setting));
    }

    [Fact]
    public void ZoneQuests_MatchMainScenarioPaths()
    {
        Dictionary<ushort, SortedSet<ushort>> found = [];
        foreach (string resource in typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
                     .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) &&
                                 x.Contains("/MSQ/", StringComparison.Ordinal) &&
                                 x.EndsWith(".json", StringComparison.Ordinal)))
        {
            var embedded = new EmbeddedQuest(resource);
            if (!ushort.TryParse(embedded.ShortName.Split('_', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out ushort questId))
                continue;

            var quest = QuestTestData.LoadQuest(new QuestId(questId), embedded);
            foreach (QuestStep step in quest.Root.QuestSequence.SelectMany(x => x.Steps)
                         .Where(x => x.InteractionType == EInteractionType.AttuneAetherCurrent))
            {
                ushort zone = (ushort)step.TerritoryId;
                if (!found.TryGetValue(zone, out SortedSet<ushort>? quests))
                    found[zone] = quests = [];
                quests.Add(questId);
            }
        }

        Assert.Equal(found.Keys.Order(), QuestData.AetherCurrentZoneQuests.Keys.Order());
        foreach ((ushort zone, SortedSet<ushort> quests) in found)
            Assert.Equal(quests, QuestData.AetherCurrentZoneQuests[zone].Order());
    }
}
