using NSubstitute;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

namespace Questionable.Tests.Validation;

public sealed class NextQuestValidatorTest
{
    private readonly NextQuestValidator _validator = new();

    private static Quest CreateQuest(ElementId questId, params QuestStep[] steps) =>
        new()
        {
            Id = questId,
            Source = Quest.ESource.Assembly,
            Root = new QuestRoot
            {
                QuestSequence =
                [
                    new QuestSequence { Sequence = 0, Steps = [..steps] }
                ]
            },
            Info = CreateInfo(questId),
        };

    private static IQuestInfo CreateInfo(ElementId questId)
    {
        var info = Substitute.For<IQuestInfo>();
        info.QuestId.Returns(questId);
        return info;
    }

    [Fact]
    public void WhenNextQuestIdReferencesSelf_ReturnsError()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId,
            new QuestStep { NextQuestId = questId });

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.InvalidNextQuestId, issues[0].Type);
        Assert.Equal(EIssueSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void WhenNextQuestIdReferencesDifferentQuest_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId,
            new QuestStep { NextQuestId = new QuestId(456) });

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void WhenNoNextQuestId_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId,
            new QuestStep { NextQuestId = null });

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void WhenMultipleStepsAndOnlyOneReferencesSelf_ReturnsOneError()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId,
            new QuestStep { NextQuestId = new QuestId(456) },
            new QuestStep { NextQuestId = questId });

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
    }
}