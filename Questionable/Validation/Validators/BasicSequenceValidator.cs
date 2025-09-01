using Questionable.Model;
using Questionable.Model.Questing;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Validation.Validators;

internal sealed class BasicSequenceValidator : IQuestValidator
{
    /// <summary>
    /// A quest should have sequences from 0 to N, and (if more than 'AcceptQuest' exists), a 255 sequence.
    /// </summary>
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        var sequences = quest.Root.QuestSequence;
        var foundStart = sequences.FirstOrDefault(x => x.Sequence == 0);
        if (foundStart == null)
        {
            yield return new ValidationIssue
            {
                ElementId = quest.Id,
                Sequence = 0,
                Step = null,
                Type = EIssueType.MissingSequence0,
                Severity = EIssueSeverity.Error,
                Description = "Missing quest start",
            };
            yield break;
        }

        if (quest.Info is QuestInfo { CompletesInstantly: true })
        {
            foreach (var sequence in sequences)
            {
                if (sequence == foundStart)
                    continue;

                yield return new ValidationIssue
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = null,
                    Type = EIssueType.InstantQuestWithMultipleSteps,
                    Severity = EIssueSeverity.Error,
                    Description = "Instant quest should not have any sequences after the start",
                };
            }
        }
        else if (quest.Info is QuestInfo)
        {
            int maxSequence = sequences.Select(x => x.Sequence)
                .Where(x => x != 255)
                .Max();

            for (int i = 0; i < maxSequence; i++)
            {
                var foundSequences = sequences.Where(x => x.Sequence == i).ToList();
                var issue = ValidateSequences(quest, i, foundSequences);
                if (issue != null)
                    yield return issue;
            }

            var foundEnding = sequences.Where(x => x.Sequence == 255).ToList();
            var endingIssue = ValidateSequences(quest, 255, foundEnding);
            if (endingIssue != null)
                yield return endingIssue;
        }
    }

    private static ValidationIssue? ValidateSequences(Quest quest, int sequenceNo, List<QuestSequence> foundSequences)
    {
        if (foundSequences.Count == 0)
        {
            return new ValidationIssue
            {
                ElementId = quest.Id,
                Sequence = (byte)sequenceNo,
                Step = null,
                Type = EIssueType.MissingSequence,
                Severity = EIssueSeverity.Error,
                Description = "Missing sequence",
            };
        }
        else if (foundSequences.Count == 2)
        {
            return new ValidationIssue
            {
                ElementId = quest.Id,
                Sequence = (byte)sequenceNo,
                Step = null,
                Type = EIssueType.DuplicateSequence,
                Severity = EIssueSeverity.Error,
                Description = "Duplicate sequence",
            };
        }
        else
            return null;
    }
}
