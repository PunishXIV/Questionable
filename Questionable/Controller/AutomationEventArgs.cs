namespace Questionable.Controller;

internal sealed class AutomationEventArgs(QuestController.EAutomationType automationType) : EventArgs
{
    public QuestController.EAutomationType AutomationType { get; } = automationType;
}
