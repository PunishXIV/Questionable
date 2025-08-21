using System.Collections.Generic;
using System.Linq;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Validation.Validators;

internal sealed class UniqueSinglePlayerInstanceValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        var singlePlayerInstances = quest.AllSteps()
            .Where(x => x.Step.InteractionType == EInteractionType.SinglePlayerDuty)
            .Select(x => (x.Sequence, x.StepId, x.Step.SinglePlayerDutyIndex))
            .ToList();
        if (singlePlayerInstances.DistinctBy(x => x.SinglePlayerDutyIndex).Count() < singlePlayerInstances.Count)
        {
            foreach (var singlePlayerInstance in singlePlayerInstances)
            {
                yield return new ValidationIssue
                {
                    ElementId = quest.Id,
                    Sequence = singlePlayerInstance.Sequence.Sequence,
                    Step = singlePlayerInstance.StepId,
                    Type = EIssueType.DuplicateSinglePlayerInstance,
                    Severity = EIssueSeverity.Error,
                    Description = $"Duplicate singleplayer duty index: {singlePlayerInstance.SinglePlayerDutyIndex}",
                };
            }
        }
    }
}
