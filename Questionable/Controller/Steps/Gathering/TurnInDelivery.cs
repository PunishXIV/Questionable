using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Gathering;

internal static class TurnInDelivery
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (quest.Id is not SatisfactionSupplyNpcId ||
                    sequence.Sequence != 1
                    )
                return null;

            return new Task();
        }
    }

    internal sealed record Task : ITask
    {
        public override string ToString() => "WeeklyDeliveryTurnIn";
    }

    internal sealed class Executor(ILogger<Executor> logger) : TaskExecutor<Task>
    {
        private ushort? _remainingAllowances;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            AgentSatisfactionSupply* agentSatisfactionSupply = AgentSatisfactionSupply.Instance();
            if (agentSatisfactionSupply == null || !agentSatisfactionSupply->IsAgentActive())
                return _remainingAllowances == null ? ETaskResult.StillRunning : ETaskResult.TaskComplete;

            uint addonId = agentSatisfactionSupply->GetAddonId();
            if (addonId == 0)
                return _remainingAllowances == null ? ETaskResult.StillRunning : ETaskResult.TaskComplete;

            AtkUnitBase* addon = AddonUtils.GetAddonById(addonId);
            if (addon == null || !AddonUtils.IsAddonReady(addon))
                return ETaskResult.StillRunning;

            ushort remainingAllowances = agentSatisfactionSupply->NpcData.RemainingAllowances;
            if (remainingAllowances == 0)
            {
                logger.LogInformation("No remaining weekly allowances");
                addon->FireCallbackInt(0);
                return ETaskResult.TaskComplete;
            }

            if (InventoryManager.Instance()->GetInventoryItemCount(agentSatisfactionSupply->Items[1].Id,
                minCollectability: (short)agentSatisfactionSupply->Items[1].Collectability1) == 0)
            {
                logger.LogInformation("Inventory has no {ItemId}", agentSatisfactionSupply->Items[1].Id);
                addon->FireCallbackInt(0);
                return ETaskResult.TaskComplete;
            }

            if (_remainingAllowances == remainingAllowances)
                return ETaskResult.StillRunning;

            logger.LogInformation("Attempting turn-in (remaining allowances: {RemainingAllowances})",
                remainingAllowances);
            _remainingAllowances = remainingAllowances;

            AtkValue* pickGatheringItem = stackalloc AtkValue[]
            {
                new() { Type = AtkValueType.Int, Int = 1 },
                new() { Type = AtkValueType.Int, Int = 1 }
            };
            addon->FireCallback(2, pickGatheringItem);
            return ETaskResult.StillRunning;
        }

        // Turn-ins need a gathering/crafting job, so fighting back isn't an option.
        public override bool ShouldInterruptOnDamage() => false;
    }
}
