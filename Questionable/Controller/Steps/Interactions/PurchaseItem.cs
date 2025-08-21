using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Interactions;

internal static class PurchaseItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.PurchaseItem)
                return null;
            throw new System.NotImplementedException();
        }
    }

    internal sealed class PurchaseRequest
    {

    }
}
