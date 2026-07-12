using System.Collections.Generic;
using System.Linq;
using Questionable.Domain;
using Questionable.Model.Questing;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Validation.Validators;

internal sealed class UniqueStartStopValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (quest.Id is SatisfactionSupplyNpcId or AlliedSocietyDailyId)
            yield break;

        List<(QuestSequence Sequence, int StepId, QuestStep Step)> questAccepts =
            FindQuestStepsWithInteractionType(quest, [EInteractionType.AcceptQuest])
                .Where(x => x.Step.PickUpQuestId == null)
                .ToList();
        foreach ((QuestSequence Sequence, int StepId, QuestStep Step) in questAccepts)
        {
            if (Sequence.Sequence != 0 || StepId != quest.FindSequence(0)!.Steps.Count - 1)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = Sequence.Sequence,
                    Step = StepId,
                    Type = EIssueType.UnexpectedAcceptQuestStep,
                    Severity = EIssueSeverity.Error,
                    Description = _L("Unexpected AcceptQuest step")
                };
            }
        }

        if (quest.FindSequence(0) != null && questAccepts.Count == 0)
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = 0,
                Step = null,
                Type = EIssueType.MissingQuestAccept,
                Severity = EIssueSeverity.Error,
                Description = _L("No AcceptQuest step")
            };
        }

        List<(QuestSequence Sequence, int StepId, QuestStep Step)> questCompletes =
            FindQuestStepsWithInteractionType(quest, [EInteractionType.CompleteQuest])
                .Where(x => x.Step.TurnInQuestId == null)
                .ToList();
        foreach ((QuestSequence Sequence, int StepId, QuestStep Step) in questCompletes)
        {
            if (Sequence.Sequence != 255 || StepId != quest.FindSequence(255)!.Steps.Count - 1)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = Sequence.Sequence,
                    Step = StepId,
                    Type = EIssueType.UnexpectedCompleteQuestStep,
                    Severity = EIssueSeverity.Error,
                    Description = _L("Unexpected CompleteQuest step")
                };
            }
        }

        if (quest.FindSequence(255) != null && questCompletes.Count == 0)
        {
            yield return new()
            {
                ElementId = quest.Id,
                Sequence = 255,
                Step = null,
                Type = EIssueType.MissingQuestComplete,
                Severity = EIssueSeverity.Error,
                Description = _L("No CompleteQuest step")
            };
        }
    }

    private static IEnumerable<(QuestSequence Sequence, int StepId, QuestStep Step)> FindQuestStepsWithInteractionType(
        Quest quest, List<EInteractionType> interactionType) => quest.AllSteps().Where(x => interactionType.Contains(x.Step.InteractionType));
}
