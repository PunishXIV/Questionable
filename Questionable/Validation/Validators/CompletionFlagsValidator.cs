using Questionable.Controller.Utils;
using Questionable.Model;
using Questionable.Model.Questing;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Questionable.Validation.Validators;

internal sealed class CompletionFlagsValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        // this maybe should check for skipconditions, but this applies to one quest only atm
        if (quest.Id.Value == 5149)
            yield break;

        foreach (var sequence in quest.AllSequences())
        {
            var mappedCompletionFlags = sequence.Steps
                .Select(x =>
                {
                    if (QuestWorkUtils.HasCompletionFlags(x.CompletionQuestVariablesFlags))
                    {
                        return Enumerable.Range(0, 6).Select(y =>
                            {
                                QuestWorkValue? value = x.CompletionQuestVariablesFlags[y];
                                if (value == null)
                                    return 0;

                                // this isn't perfect, as it assumes {High: 1, Low: null} == {High: 1, Low: 0}
                                return (long)BitOperations.RotateLeft(
                                    (ulong)(value.High.GetValueOrDefault() * 16 + value.Low.GetValueOrDefault()), 8 * y);
                            })
                            .Sum();
                    }
                    else
                        return 0;
                })
                .ToList();

            for (int i = 0; i < sequence.Steps.Count; ++i)
            {
                var flags = mappedCompletionFlags[i];
                if (flags == 0)
                    continue;

                if (mappedCompletionFlags.Count(x => x == flags) >= 2)
                {
                    yield return new ValidationIssue
                    {
                        ElementId = quest.Id,
                        Sequence = sequence.Sequence,
                        Step = i,
                        Type = EIssueType.DuplicateCompletionFlags,
                        Severity = EIssueSeverity.Error,
                        Description =
                            $"Duplicate completion flags: {string.Join(", ", sequence.Steps[i].CompletionQuestVariablesFlags)}",
                    };
                }
            }
        }
    }
}
