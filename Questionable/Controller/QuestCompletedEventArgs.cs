namespace Questionable.Controller;

internal sealed class QuestCompletedEventArgs(ElementId questId) : EventArgs
{
    public ElementId QuestId { get; } = questId;
}
