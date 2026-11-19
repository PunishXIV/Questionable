using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Validation;

public sealed class NextQuestValidatorTest
{
    private readonly NextQuestValidator _validator = new();

    [Fact]
    public void WhenNextQuestIdReferencesSelf_ReturnsError()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = questId }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.InvalidNextQuestId, issues[0].Type);
        Assert.Equal(EIssueSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void WhenNextQuestIdReferencesDifferentQuest_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = new QuestId(456) }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void WhenNoNextQuestId_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = null }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void WhenMultipleStepsAndOnlyOneReferencesSelf_ReturnsOneError()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId,
            Seq(0,
                new QuestStep { NextQuestId = new QuestId(456) },
                new QuestStep { NextQuestId = questId }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
    }
}