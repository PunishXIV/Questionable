namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, NextQuestValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class NextQuestValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach ((QuestSequence sequence, int stepId, QuestStep step) in quest.AllSteps().Where(x => x.Step.NextQuestId == quest.Id))
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = sequence.Sequence,
                Step = stepId,
                Type = EIssueType.InvalidNextQuestId,
                Severity = EIssueSeverity.Error,
                Description = _L("Next quest should not reference itself")
            };
        }
    }
}
