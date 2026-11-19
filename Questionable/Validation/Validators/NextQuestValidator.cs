using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, NextQuestValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class NextQuestValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach ((QuestSequence Sequence, int StepId, QuestStep Step) in quest.AllSteps().Where(x => x.Step.NextQuestId == quest.Id))
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = Sequence.Sequence,
                Step = StepId,
                Type = EIssueType.InvalidNextQuestId,
                Severity = EIssueSeverity.Error,
                Description = _L("Next quest should not reference itself")
            };
        }
    }
}
