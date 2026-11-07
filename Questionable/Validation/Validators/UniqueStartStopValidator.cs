using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, UniqueStartStopValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class UniqueStartStopValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (quest.Id is SatisfactionSupplyNpcId or AlliedSocietyDailyId)
            yield break;

        List<(QuestSequence Sequence, int StepId, QuestStep Step)> questAccepts =
            FindStepsOfType(quest, [EInteractionType.AcceptQuest])
                .Where(x => x.Step.PickUpQuestId == null)
                .ToList();
        foreach ((QuestSequence sequence, int stepId, QuestStep step) in questAccepts)
        {
            if (sequence.Sequence != 0 || stepId != quest.FindSequence(0)!.Steps.Count - 1)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = stepId,
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
            FindStepsOfType(quest, [EInteractionType.CompleteQuest])
                .Where(x => x.Step.TurnInQuestId == null)
                .ToList();
        foreach ((QuestSequence sequence, int stepId, QuestStep step) in questCompletes)
        {
            if (sequence.Sequence != 255 || stepId != quest.FindSequence(255)!.Steps.Count - 1)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = stepId,
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

    private static IEnumerable<(QuestSequence Sequence, int StepId, QuestStep Step)> FindStepsOfType(
        Quest quest, List<EInteractionType> types) => quest.AllSteps().Where(x => types.Contains(x.Step.InteractionType));
}
