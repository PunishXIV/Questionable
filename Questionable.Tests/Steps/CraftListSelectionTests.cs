using NSubstitute;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftListSelectionTests
{
    private static readonly IReadOnlySet<uint> Culinarian268Outputs = new HashSet<uint> { 4712, 4733, 4749 };

    [Fact]
    public void NatronStep_DoesNotSubmitQuestListThatConsumesItsTarget()
    {
        string resource = typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("/268_Diplomacy of the Skillet.json", StringComparison.Ordinal));
        var quest = QuestTestData.LoadQuest(new QuestId(268), new EmbeddedQuest(resource));
        var sequence = quest.Root.QuestSequence.Single(x => x.Steps.Any(s =>
            s.InteractionType == EInteractionType.Craft && s.ItemId == 5522));
        var step = sequence.Steps.Single(x => x.InteractionType == EInteractionType.Craft && x.ItemId == 5522);
        var task = new Craft.CraftTask(quest, sequence.Sequence, step);
        var ipc = Substitute.For<IArtisanIpc>();

        Assert.False(Craft.TryStartQuestList(ipc, task.Quest.Id, step.ItemId, Culinarian268Outputs));
        Assert.Empty(ipc.ReceivedCalls());
        var target = Assert.Single(Craft.GetItemTargets(task));
        Assert.Equal(5522u, target.ItemId);
        Assert.Equal(1, target.ItemCount);
    }

    [Theory]
    [InlineData(4712u)]
    [InlineData(4733u)]
    [InlineData(4749u)]
    public void QuestFinalOutputs_StillUseExistingList(uint itemId)
    {
        var ipc = Substitute.For<IArtisanIpc>();
        ipc.CraftList(65804).Returns(ArtisanStartResult.Submitted);

        Assert.True(Craft.TryStartQuestList(ipc, new QuestId(268), itemId, Culinarian268Outputs));
        ipc.Received(1).CraftList(65804);
        Assert.Single(ipc.ReceivedCalls());
    }

    [Fact]
    public void MissingRequirementMetadata_DoesNotSubmitAnUnprovenList()
    {
        var ipc = Substitute.For<IArtisanIpc>();
        ipc.CraftList(65804).Returns(ArtisanStartResult.Submitted);

        Assert.False(Craft.TryStartQuestList(ipc, new QuestId(268), 5522, new HashSet<uint>()));
        Assert.Empty(ipc.ReceivedCalls());
    }

    [Fact]
    public void RequiredItemSymbols_ExcludeEventItemsAndUnrelatedConstants()
    {
        var items = QuestCraftingRequirements.GetRequiredItemIds(
        [
            ("RITEM0", 4712), ("RITEM1", 4733), ("RITEM2", 4749),
            ("ITEM0", 5522), ("RITEM_QTY", 12), ("RITEM_HQ", 1),
            ("RITEM", 42), ("RITEM3", 0), ("RITEM-1", 43), ("RITEM1", 4733),
        ]);

        Assert.True(items.SetEquals(Culinarian268Outputs));
    }
}
