using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Validation;

public sealed class UniqueStartStopValidatorTest
{
    private readonly UniqueStartStopValidator _validator = new();

    // --- helpers ---

    private static QuestStep Step(EInteractionType type,
        ElementId? pickUpQuestId = null, ElementId? turnInQuestId = null) =>
        new() { InteractionType = type, PickUpQuestId = pickUpQuestId, TurnInQuestId = turnInQuestId };

    // --- early exits ---

    [Fact]
    public void SatisfactionSupplyQuest_ReturnsNoIssues()
    {
        var quest = CreateQuest(new SatisfactionSupplyNpcId(1),
            Seq(0, Step(EInteractionType.WalkTo)),
            Seq(255, Step(EInteractionType.WalkTo)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void AlliedSocietyDailyQuest_ReturnsNoIssues()
    {
        var quest = CreateQuest(new AlliedSocietyDailyId(1),
            Seq(0, Step(EInteractionType.WalkTo)),
            Seq(255, Step(EInteractionType.WalkTo)));

        Assert.Empty(_validator.Validate(quest));
    }

    // --- AcceptQuest ---

    [Fact]
    public void AcceptQuestAsLastStepOfSequence0_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0,
                Step(EInteractionType.WalkTo),
                Step(EInteractionType.AcceptQuest)),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void AcceptQuestNotLastStepOfSequence0_ReturnsError()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0,
                Step(EInteractionType.AcceptQuest),
                Step(EInteractionType.WalkTo)),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.UnexpectedAcceptQuestStep, issues[0].Type);
    }

    [Fact]
    public void AcceptQuestInWrongSequence_ReturnsError()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.WalkTo)),
            Seq(1, Step(EInteractionType.AcceptQuest)),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.UnexpectedAcceptQuestStep, issues[0].Type);
    }

    [Fact]
    public void MissingAcceptQuest_ReturnsError()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.WalkTo)),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.MissingQuestAccept, issues[0].Type);
    }

    [Fact]
    public void AcceptQuestWithPickUpQuestId_IsIgnored()
    {
        // A nested quest-accept (PickUpQuestId set) should not count
        // as the quest's own AcceptQuest, so MissingQuestAccept is expected
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest, pickUpQuestId: new QuestId(99))),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Contains(issues, x => x.Type == EIssueType.MissingQuestAccept);
        Assert.DoesNotContain(issues, x => x.Type == EIssueType.UnexpectedAcceptQuestStep);
    }

    // --- CompleteQuest ---

    [Fact]
    public void CompleteQuestAsLastStepOfSequence255_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest)),
            Seq(255,
                Step(EInteractionType.WalkTo),
                Step(EInteractionType.CompleteQuest)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void CompleteQuestNotLastStepOfSequence255_ReturnsError()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest)),
            Seq(255,
                Step(EInteractionType.CompleteQuest),
                Step(EInteractionType.WalkTo)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.UnexpectedCompleteQuestStep, issues[0].Type);
    }

    [Fact]
    public void MissingCompleteQuest_ReturnsError()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest)),
            Seq(255, Step(EInteractionType.WalkTo)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.MissingQuestComplete, issues[0].Type);
    }

    [Fact]
    public void CompleteQuestWithTurnInQuestId_IsIgnored()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest)),
            Seq(255, Step(EInteractionType.CompleteQuest, turnInQuestId: new QuestId(99))));

        var issues = _validator.Validate(quest).ToList();

        Assert.Contains(issues, x => x.Type == EIssueType.MissingQuestComplete);
        Assert.DoesNotContain(issues, x => x.Type == EIssueType.UnexpectedCompleteQuestStep);
    }

    // --- no sequence 0 / 255 ---

    [Fact]
    public void NoSequence0_NoMissingAcceptIssue()
    {
        // Guard: MissingQuestAccept is only reported when sequence 0 exists
        var quest = CreateQuest(new QuestId(1),
            Seq(255, Step(EInteractionType.CompleteQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.DoesNotContain(issues, x => x.Type == EIssueType.MissingQuestAccept);
    }

    [Fact]
    public void NoSequence255_NoMissingCompleteIssue()
    {
        // Guard: MissingQuestComplete is only reported when sequence 255 exists
        var quest = CreateQuest(new QuestId(1),
            Seq(0, Step(EInteractionType.AcceptQuest)));

        var issues = _validator.Validate(quest).ToList();

        Assert.DoesNotContain(issues, x => x.Type == EIssueType.MissingQuestComplete);
    }
}