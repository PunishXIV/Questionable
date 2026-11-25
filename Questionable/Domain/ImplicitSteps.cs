namespace Questionable.Domain;

internal static class ImplicitSteps
{
    internal static bool Fill(QuestRoot root, IQuestInfo info) =>
        info is QuestInfo quest
            ? Fill(root, quest.IssuerDataId, quest.TargetEnd, quest.IssuerLocation.Position, quest.IssuerLocation.TerritoryId)
            : !root.QuestSequence.Any(IsEmpty);

    private static bool IsEmpty(QuestSequence sequence) => sequence.Sequence is 0 or 255 && sequence.Steps.Count == 0;

    // An empty sequence 0 or 255 means "plain accept/turn-in at the quest giver"; a missing one stays missing.
    internal static bool Fill(QuestRoot root, uint issuer, uint turnIn, Vector3 position, uint territory)
    {
        bool filled = true;
        foreach (QuestSequence sequence in root.QuestSequence.Where(IsEmpty))
        {
            EInteractionType? type = sequence.Sequence switch
            {
                0 => EInteractionType.AcceptQuest,
                255 when turnIn == issuer => EInteractionType.CompleteQuest,
                _ => null,
            };
            if (type != null && issuer > 0 && territory > 0)
                sequence.Steps.Add(new QuestStep(type.Value, issuer, position, territory));
            else
                filled = false;
        }

        return filled;
    }
}
