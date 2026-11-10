namespace Questionable.Functions;

internal sealed record PriorityQuestInfo(ElementId QuestId, string? UnavailableReason = null)
{
    public bool IsAvailable => UnavailableReason == null;
}
