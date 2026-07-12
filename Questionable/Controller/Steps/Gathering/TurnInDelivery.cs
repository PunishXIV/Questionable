using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Utils;
namespace Questionable.Controller.Steps.Gathering;

internal static class TurnInDelivery
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (quest.Id is not SatisfactionSupplyNpcId || sequence.Sequence != 1)
                return null;

            return new Task();
        }
    }

    internal sealed record Task : ITask
    {
        public override string ToString() => "WeeklyDeliveryTurnIn";
    }

    internal sealed class SatisfactionSupplyTurnIn(ILogger<SatisfactionSupplyTurnIn> logger) : TaskExecutor<Task>
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

            // we should at least wait until we have less allowances
            if (_remainingAllowances == remainingAllowances)
                return ETaskResult.StillRunning;

            // try turning it in...
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

        // not even sure if any turn-in npcs are NEAR mobs; but we also need to be on a gathering/crafting job
        public override bool ShouldInterruptOnDamage() => false;
    }
}
