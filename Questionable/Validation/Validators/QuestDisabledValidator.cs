using System.Collections.Generic;
using Questionable.Domain;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Validation.Validators;

internal sealed class QuestDisabledValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (quest.Root.Disabled)
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
