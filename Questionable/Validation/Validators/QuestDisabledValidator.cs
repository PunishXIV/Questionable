using System.Collections.Generic;
using Questionable.Model;

namespace Questionable.Validation.Validators;

internal sealed class QuestDisabledValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (quest.Root.Disabled)
        {
            yield return new ValidationIssue
            {
                ElementId = quest.Id,
                Sequence = null,
                Step = null,
                Type = EIssueType.QuestDisabled,
                Severity = EIssueSeverity.None,
                Description = "Quest is disabled",
            };
        }
    }
}
