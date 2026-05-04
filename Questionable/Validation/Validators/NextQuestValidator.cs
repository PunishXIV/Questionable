using Questionable.Model;
using Questionable.Model.Questing;
using System.Collections.Generic;
using System.Linq;
namespace Questionable.Validation.Validators;

internal sealed class NextQuestValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach((QuestSequence Sequence, int StepId, QuestStep Step) invalidNextQuest in quest.AllSteps().Where(x => x.Step.NextQuestId == quest.Id))
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = invalidNextQuest.Sequence.Sequence,
                Step = invalidNextQuest.StepId,
                Type = EIssueType.InvalidNextQuestId,
                Severity = EIssueSeverity.Error,
                Description = "Next quest should not reference itself"
            };
        }
    }
}
