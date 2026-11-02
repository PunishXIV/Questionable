using NSubstitute;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ProviderTravelTests
{
    private readonly IFeeshIpc _feeshIpc = Substitute.For<IFeeshIpc>();

    public ProviderTravelTests()
    {
        _feeshIpc.IsAvailable().Returns(true);
        _feeshIpc.CanCatch(Arg.Any<uint>()).Returns(true);
    }

    private static QuestStep Step(EInteractionType type, List<QuestWorkValue?>? completionQuestVariablesFlags = null,
        uint? itemId = null) =>
        new()
        {
            InteractionType = type,
            CompletionQuestVariablesFlags = completionQuestVariablesFlags ?? [],
            ItemsToGather = itemId is { } id ? [new GatheredItem { ItemId = id, ItemCount = 1 }] : [],
        };

    [Fact]
    public void WalkStepsLeadingToFeeshFishing_SkipQuestionableTravel()
    {
        // FSH 1109: buy bait, two "Move to fishing spot" steps, then fish.
        QuestStep purchase = Step(EInteractionType.PurchaseItem), first = Step(EInteractionType.WalkTo),
            second = Step(EInteractionType.WalkTo), fish = Step(EInteractionType.Fish, itemId: 4874);
        var sequence = new QuestSequence { Steps = [purchase, first, second, fish] };

        Assert.True(ProviderTravel.IsHandled(sequence, first, _feeshIpc));
        Assert.True(ProviderTravel.IsHandled(sequence, second, _feeshIpc));
        Assert.True(ProviderTravel.IsHandled(sequence, fish, _feeshIpc));
        Assert.False(ProviderTravel.IsHandled(sequence, purchase, _feeshIpc));
    }

    [Fact]
    public void WalkStepsBeforeFishFeeshCannotCatch_KeepQuestionableTravel()
    {
        _feeshIpc.CanCatch(1509).Returns(false);
        QuestStep walk = Step(EInteractionType.WalkTo), fish = Step(EInteractionType.Fish, itemId: 1509);
        var sequence = new QuestSequence { Steps = [walk, fish] };

        Assert.False(ProviderTravel.IsHandled(sequence, walk, _feeshIpc));
        Assert.False(ProviderTravel.IsHandled(sequence, fish, _feeshIpc));
    }

    [Fact]
    public void GatherStepsAndTheWalkStepsBeforeThem_SkipQuestionableTravel()
    {
        // MIN 646: walk, an empty step, then gather in Ul'dah.
        QuestStep walk = Step(EInteractionType.WalkTo), empty = Step(EInteractionType.None),
            gather = Step(EInteractionType.Gather, itemId: 5121), turnIn = Step(EInteractionType.Interact);
        var sequence = new QuestSequence { Steps = [walk, empty, gather, turnIn] };

        Assert.True(ProviderTravel.IsHandled(sequence, walk, _feeshIpc));
        Assert.True(ProviderTravel.IsHandled(sequence, empty, _feeshIpc));
        Assert.True(ProviderTravel.IsHandled(sequence, gather, _feeshIpc));
        Assert.False(ProviderTravel.IsHandled(sequence, turnIn, _feeshIpc));
    }

    [Fact]
    public void GatherStepNamingAQuestNode_KeepsQuestionableTravel()
    {
        // Qhoterl's Chronicles (3833) walks to specific quest nodes.
        QuestStep walk = Step(EInteractionType.WalkTo), gather = Step(EInteractionType.Gather, itemId: 29589);
        gather.DataId = 33005;
        var sequence = new QuestSequence { Steps = [walk, gather] };

        Assert.False(ProviderTravel.IsHandled(sequence, walk, _feeshIpc));
        Assert.False(ProviderTravel.IsHandled(sequence, gather, _feeshIpc));
    }

    [Fact]
    public void WalkStepsWithObjectivesOrBeforeOtherSteps_KeepQuestionableTravel()
    {
        List<QuestWorkValue?> flags = [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null];
        QuestStep flagged = Step(EInteractionType.WalkTo, flags), plain = Step(EInteractionType.WalkTo);
        var sequence = new QuestSequence
        {
            Steps = [flagged, Step(EInteractionType.Gather, itemId: 5121), plain, Step(EInteractionType.Interact)],
        };

        Assert.False(ProviderTravel.IsHandled(sequence, flagged, _feeshIpc));
        Assert.False(ProviderTravel.IsHandled(sequence, plain, _feeshIpc));
    }
}
