using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Functions;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Questionable.Utils;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
namespace Questionable.Controller.Steps.Gathering;

internal static class DoGatherCollectable
{
    internal sealed record Task
    (
        GatheringController.GatheringRequest Request,
        GatheringNode Node,
        bool RevisitRequired) : ITask, IRevisitAware
    {
        public bool RevisitTriggered { get; private set; }

        public void OnRevisit()
        {
            RevisitTriggered = true;
        }

        public override string ToString()
        {
            return $"DoGatherCollectable({SeIconChar.Collectible.ToIconString()}/{Request.Collectability}){(RevisitRequired ? " if revist" : "")}";
        }
    }

    internal sealed class GatherCollectableExecutor
    (
        GatheringController gatheringController,
        GameFunctions gameFunctions,
        IObjectTable objectTable,
        IGameGuiAdapter gameGui,
        ILogger<GatherCollectableExecutor> logger) : TaskExecutor<Task>
    {
        private Queue<EAction>? _actionQueue;

        private bool? _expectedScrutiny;

        protected override bool Start()
        {
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            if (Task.RevisitRequired && !Task.RevisitTriggered)
            {
                logger.LogInformation("No revisit");
                return ETaskResult.TaskComplete;
            }

            if (gatheringController.HasNodeDisappeared(Task.Node))
            {
                logger.LogInformation("Node disappeared");
                return ETaskResult.TaskComplete;
            }

            if (gatheringController.HasRequestedItems())
            {
                if (gameGui.TryGetAddonByName("GatheringMasterpiece", out AtkUnitBase* atkUnitBase))
                {
                    atkUnitBase->FireCallbackInt(1);
                    return ETaskResult.StillRunning;
                }

                if (gameGui.TryGetAddonByName("Gathering", out atkUnitBase))
                {
                    atkUnitBase->FireCallbackInt(-1);
                    return ETaskResult.TaskComplete;
                }
            }

            if (GameFunctions.GetFreeInventorySlots() == 0)
            {
                throw new TaskException("Inventory full");
            }

            NodeCondition? nodeCondition = GetNodeCondition();
            if (nodeCondition == null)
            {
                return ETaskResult.TaskComplete;
            }

            if (_expectedScrutiny != null)
            {
                if (nodeCondition.ScrutinyActive != _expectedScrutiny)
                {
                    return ETaskResult.StillRunning;
                }

                // continue on next frame
                _expectedScrutiny = null;
                return ETaskResult.StillRunning;
            }

            if (_actionQueue != null && _actionQueue.TryPeek(out EAction nextAction))
            {
                if (gameFunctions.UseAction(nextAction))
                {
                    _expectedScrutiny = nextAction switch
                    {
                        EAction.ScrutinyMiner or EAction.ScrutinyBotanist => true,
                        EAction.ScourMiner or EAction.ScourBotanist or EAction.MeticulousMiner
                            or EAction.MeticulousBotanist => false,
                        var _ => null
                    };
                    logger.LogInformation("Used action {Action} on node", nextAction);
                    _actionQueue.Dequeue();
                }

                return ETaskResult.StillRunning;
            }

            if (nodeCondition.CollectabilityToGoal(Task.Request.Collectability) > 0)
            {
                _actionQueue = GetNextActions(nodeCondition);
                if (_actionQueue != null)
                {
                    foreach(EAction action in _actionQueue)
                    {
                        logger.LogInformation("Next Actions {Action}", action);
                    }
                    return ETaskResult.StillRunning;
                }
            }

            _actionQueue = new();
            _actionQueue.Enqueue(PickAction(EAction.CollectMiner, EAction.CollectBotanist));
            return ETaskResult.StillRunning;
        }

        private unsafe NodeCondition? GetNodeCondition()
        {
            if (gameGui.TryGetAddonByName("GatheringMasterpiece", out AtkUnitBase* atkUnitBase))
            {
                AtkValue* atkValues = atkUnitBase->AtkValues;
                return new(
                    atkValues[13].UInt,
                    atkValues[14].UInt,
                    atkValues[62].UInt,
                    atkValues[63].UInt,
                    atkValues[54].Bool,
                    atkValues[48].UInt,
                    atkValues[51].UInt
                );
            }

            return null;
        }

        private Queue<EAction> GetNextActions(NodeCondition nodeCondition)
        {
            Queue<EAction> actions = new();

            if (objectTable[0] == null)
            {
                return actions;
            }

            uint gp = ((IPlayerCharacter)objectTable[0]!).CurrentGp;
            logger.LogTrace(
                "Getting next actions (with {GP} GP, {MeticulousCollectability}~ meticulous, {ScourCollectability}~ scour)",
                gp, nodeCondition.CollectabilityFromMeticulous, nodeCondition.CollectabilityFromScour);


            uint neededCollectability = nodeCondition.CollectabilityToGoal(Task.Request.Collectability);
            if (neededCollectability <= nodeCondition.CollectabilityFromMeticulous)
            {
                logger.LogTrace("Can get all needed {NeededCollectability} from {Collectability}~ meticulous",
                    neededCollectability, nodeCondition.CollectabilityFromMeticulous);
                actions.Enqueue(PickAction(EAction.MeticulousMiner, EAction.MeticulousBotanist));
                return actions;
            }

            if (neededCollectability <= nodeCondition.CollectabilityFromScour)
            {
                logger.LogTrace("Can get all needed {NeededCollectability} from {Collectability}~ scour",
                    neededCollectability, nodeCondition.CollectabilityFromScour);
                actions.Enqueue(PickAction(EAction.ScourMiner, EAction.ScourBotanist));
                return actions;
            }

            // neither action directly solves our problem
            if (!nodeCondition.ScrutinyActive && gp >= 200)
            {
                logger.LogTrace("Still missing {NeededCollectability} collectability, scrutiny inactive",
                    neededCollectability);
                actions.Enqueue(PickAction(EAction.ScrutinyMiner, EAction.ScrutinyBotanist));
                return actions;
            }

            if (nodeCondition.ScrutinyActive)
            {
                logger.LogTrace(
                    "Scrutiny active, need {NeededCollectability} and we expect {Collectability}~ meticulous",
                    neededCollectability, nodeCondition.CollectabilityFromMeticulous);
                actions.Enqueue(PickAction(EAction.MeticulousMiner, EAction.MeticulousBotanist));
                return actions;
            }
            else
            {
                logger.LogTrace("Scrutiny active, need {NeededCollectability} and we expect {Collectability}~ scour",
                    neededCollectability, nodeCondition.CollectabilityFromScour);
                actions.Enqueue(PickAction(EAction.ScourMiner, EAction.ScourBotanist));
                return actions;
            }
        }

        private unsafe EAction PickAction(EAction minerAction, EAction botanistAction)
        {
            if ((Job?)PlayerState.Instance()->CurrentClassJobId == Job.MIN)
            {
                return minerAction;
            }
            else
            {
                return botanistAction;
            }
        }

        public override bool ShouldInterruptOnDamage()
        {
            return false;
        }
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    private sealed record NodeCondition
    (
        uint CurrentCollectability,
        uint MaxCollectability,
        uint CurrentIntegrity,
        uint MaxIntegrity,
        bool ScrutinyActive,
        uint CollectabilityFromScour,
        uint CollectabilityFromMeticulous)
    {
        public uint CollectabilityToGoal(uint goal)
        {
            if (goal >= CurrentCollectability)
            {
                return goal - CurrentCollectability;
            }
            return CurrentCollectability == 0 ? 1u : 0u;
        }
    }
}
