using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, QuestDisabledValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class QuestDisabledValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        _ = !quest.AllSequences().TryGetFirst(out QuestSequence? seq);
        bool firstSequenceHasNotice = seq?.Steps.Any(step => step.InteractionType is EInteractionType.Instruction && step.Comment != null) ?? false;
        if (quest.Root.Disabled && !firstSequenceHasNotice)
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = null,
                Step = null,
                Type = EIssueType.QuestDisabled,
                Severity = EIssueSeverity.None,
                Description = _L("Quest is disabled")
            };
        }
    }
}
