namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, UniqueQuestBattleValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class UniqueQuestBattleValidator : IQuestValidator
{
    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        List<(QuestSequence Sequence, int StepId, byte BattleIndex)> questBattles = quest.AllSteps()
            .Where(x => x.Step.InteractionType == EInteractionType.SinglePlayerDuty)
            .Select(x => (x.Sequence, x.StepId, x.Step.SinglePlayerDutyIndex))
            .ToList();
        if (questBattles.DistinctBy(x => x.BattleIndex).Count() < questBattles.Count)
        {
            foreach ((QuestSequence sequence, int stepId, byte battleIndex) in questBattles)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = stepId,
                    Type = EIssueType.DuplicateSinglePlayerInstance,
                    Severity = EIssueSeverity.Error,
                    Description = _LF("Duplicate singleplayer duty index: {0}", battleIndex)
                };
            }
        }
    }
}
