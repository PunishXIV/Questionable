using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Validation;

public sealed class NextQuestValidatorTests
{
    private readonly NextQuestValidator _validator = new();

    [Fact]
    public void NextQuestIsSelf_ReturnsError()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = questId }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.InvalidNextQuestId, issues[0].Type);
        Assert.Equal(EIssueSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void NextQuestIsOther_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = new QuestId(456) }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void NoNextQuest_ReturnsNoIssues()
    {
        var questId = new QuestId(123);
        var quest = CreateQuest(questId, Seq(0, new QuestStep { NextQuestId = null }));

        var issues = _validator.Validate(quest).ToList();

        Assert.Empty(issues);
    }

    [Fact]
    public void OneStepIsSelf_ReturnsOneError()
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

public sealed class DialogueChoiceValidatorTests
{
    private readonly DialogueChoiceValidator _validator = new(
        new ExcelFunctions(Substitute.For<IDataManager>(), NullLogger<ExcelFunctions>.Instance));

    [Fact]
    public void InvalidRowId_ReturnsError()
    {
        var step = new QuestStep
        {
            DialogueChoices = [new DialogueChoice { ExcelSheet = "quest/000/test", Prompt = ExcelRef.FromRowId(5) }]
        };
        var quest = CreateQuest(new QuestId(123), Seq(0, step));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.InvalidExcelRef, issues[0].Type);
        Assert.Equal(EIssueSeverity.Error, issues[0].Severity);
        Assert.Contains("5", issues[0].Description, StringComparison.Ordinal);
    }
}
