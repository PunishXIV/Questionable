using System.Diagnostics.CodeAnalysis;
using Dalamud.Game.ClientState.Conditions;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Gathering;

// TODO: refactor — heavy nesting (24 lines indented ≥6 levels, max indent 10 levels).
internal static class DoGather
{
    internal sealed record Task
    (
        GatheringController.GatheringRequest Request,
        GatheringNode Node,
        bool RevisitRequired) : ITask, IRevisitAware
    {
        public bool RevisitTriggered { get; private set; }

        public void OnRevisit() => RevisitTriggered = true;

        public override string ToString() => $"DoGather{(RevisitRequired ? " if revisit" : "")}";
    }

    internal sealed class GatherExecutor
    (
        GatheringController gatheringController,
        GameFunctions gameFunctions,
        IGameGuiAdapter gameGui,
        ICondition condition,
        ILogger<GatherExecutor> logger) : TaskExecutor<Task>
    {
        private Queue<EAction>? _actionQueue;
        private SlotInfo? _slotToGather;
        private bool _usedLuck;
        private bool _wasGathering;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            if (Task is { RevisitRequired: true, RevisitTriggered: false })
            {
                logger.LogInformation("No revisit");
                return ETaskResult.TaskComplete;
            }

            if (gatheringController.HasNodeDisappeared(Task.Node))
            {
                logger.LogInformation("Node disappeared");
                return ETaskResult.TaskComplete;
            }

            if (GameFunctions.GetFreeInventorySlots() == 0)
                throw new TaskException("Inventory full");

            if (condition[ConditionFlag.Gathering])
            {
                if (gameGui.TryGetAddonByName("GatheringMasterpiece", out AtkUnitBase* _))
                    return ETaskResult.TaskComplete;

                _wasGathering = true;

                if (gameGui.TryGetAddonByName("Gathering", out AddonGathering* addonGathering))
                {
                    if (gatheringController.HasRequestedItems())
                        addonGathering->FireCallbackInt(-1);
                    else
                    {
                        List<SlotInfo> slots = ReadSlots(addonGathering);
                        if (Task.Request.Collectability > 0)
                        {
                            SlotInfo slot = slots.Single(x => x.ItemId == Task.Request.ItemId);
                            logger.LogDebug("Collectible=true, clicking {Index} {ItemId}", slot.Index, slot.ItemId);
                            addonGathering->FireCallbackInt(slot.Index);
                        }
                        else
                        {
                            NodeCondition nodeCondition = new(
                                addonGathering->AtkValues[109].UInt,
                                addonGathering->AtkValues[110].UInt);
                            logger.LogDebug("NodeCondition: {CurrentIntegrity}/{MaxIntegrity}", nodeCondition.CurrentIntegrity, nodeCondition.MaxIntegrity);

                            if (_actionQueue != null && _actionQueue.TryPeek(out EAction nextAction))
                            {
                                if (gameFunctions.UseAction(nextAction))
                                {
                                    logger.LogDebug("Action: {NextAction}", nextAction);
                                    _actionQueue.Dequeue();
                                }

                                return ETaskResult.StillRunning;
                            }

                            _actionQueue = GetNextActions(nodeCondition, slots);
                            if (_actionQueue == null)
                            {
                                logger.LogDebug("No actions returned by GetNextActions");
                                addonGathering->FireCallbackInt(-1);
                                return ETaskResult.TaskComplete;
                            }

                            if (_actionQueue.Count == 0)
                            {
                                SlotInfo? slot = _slotToGather ?? slots.SingleOrDefault(x => x.ItemId == Task.Request.ItemId) ?? slots.MinBy(x => x.ItemId);
                                if (slot?.ItemId is >= 2 and <= 19)
                                {
                                    InventoryManager* inventoryManager = InventoryManager.Instance();
                                    if (inventoryManager->GetInventoryItemCount(slot.ItemId) == 9999)
                                        slot = null;
                                }

                                if (slot != null)
                                    addonGathering->FireCallbackInt(slot.Index);
                                else
                                    addonGathering->FireCallbackInt(-1);
                            }
                        }
                    }
                }
            }

            return _wasGathering && !condition[ConditionFlag.Gathering]
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        private unsafe List<SlotInfo> ReadSlots(AddonGathering* addonGathering)
        {
            List<SlotInfo> slots = [];
            for (int i = 0; i < 8; ++i)
            {
                // +8 = new item?
                uint itemId = addonGathering->ItemIds[i];
                if (itemId == 0)
                    continue;

                AtkComponentCheckBox* atkCheckbox = addonGathering->GatheredItemComponentCheckbox[i].Value;

                AtkTextNode* atkGatheringChance = atkCheckbox->UldManager.SearchNodeById(10)->GetAsAtkTextNode();
                if (!int.TryParse(atkGatheringChance->NodeText.ToString(), out int gatheringChance))
                    gatheringChance = 0;

                AtkTextNode* atkBoonChance = atkCheckbox->UldManager.SearchNodeById(16)->GetAsAtkTextNode();
                if (!int.TryParse(atkBoonChance->NodeText.ToString(), out int boonChance))
                    boonChance = 0;

                AtkComponentNode* atkImage = atkCheckbox->UldManager.SearchNodeById(31)->GetAsAtkComponentNode();
                AtkTextNode* atkQuantity = atkImage->Component->UldManager.SearchNodeById(7)->GetAsAtkTextNode();
                if (!atkQuantity->IsVisible() || !int.TryParse(atkQuantity->NodeText.ToString(), out int quantity))
                    quantity = 1;

                SlotInfo slot = new(i, itemId, gatheringChance, boonChance, quantity);
                slots.Add(slot);
            }

            logger.LogDebug("Slots: {Slots}", string.Join(", ", slots));
            return slots;
        }

        [SuppressMessage("ReSharper", "UnusedParameter.Local")]
        private Queue<EAction>? GetNextActions(NodeCondition nodeCondition, List<SlotInfo> slots)
        {
            // it's possible the item has disappeared
            if (_slotToGather != null && slots.All(x => x.Index != _slotToGather.Index))
                _slotToGather = null;

            //uint gp = objectTable.CurrentGp;
            Queue<EAction> actions = new();

            //if (!gameFunctions.HasStatus(EStatus.GatheringRateUp))
            //{
            // do we have an alternative item? only happens for 'evaluation' leve quests
            if (Task.Request.AlternativeItemId != 0)
            {
                SlotInfo alternativeSlot = slots.Single(x => x.ItemId == Task.Request.AlternativeItemId);

                if (alternativeSlot.GatheringChance == 100)
                {
                    _slotToGather = alternativeSlot;
                    return actions;
                }

                if (alternativeSlot.GatheringChance > 0)
                {
                    if (alternativeSlot.GatheringChance >= 95 &&
                        CanUseAction(EAction.SharpVision1, EAction.FieldMastery1))
                    {
                        _slotToGather = alternativeSlot;
                        logger.LogDebug("GatheringChance != 100, >= 95, using SharpVision1/FieldMastery1");
                        actions.Enqueue(PickAction(EAction.SharpVision1, EAction.FieldMastery1));
                        return actions;
                    }

                    if (alternativeSlot.GatheringChance >= 85 &&
                        CanUseAction(EAction.SharpVision2, EAction.FieldMastery2))
                    {
                        _slotToGather = alternativeSlot;
                        logger.LogDebug("GatheringChance != 100, >= 85, using SharpVision2/FieldMastery2");
                        actions.Enqueue(PickAction(EAction.SharpVision2, EAction.FieldMastery2));
                        return actions;
                    }

                    if (alternativeSlot.GatheringChance >= 50 &&
                        CanUseAction(EAction.SharpVision3, EAction.FieldMastery3))
                    {
                        _slotToGather = alternativeSlot;
                        logger.LogDebug("GatheringChance != 100, >= 50, using SharpVision3/FieldMastery3");
                        actions.Enqueue(PickAction(EAction.SharpVision3, EAction.FieldMastery3));
                        return actions;
                    }
                }
            }

            SlotInfo? slot = slots.SingleOrDefault(x => x.ItemId == Task.Request.ItemId);
            if (slot == null)
            {
                if (!_usedLuck &&
                    nodeCondition.CurrentIntegrity == nodeCondition.MaxIntegrity &&
                    CanUseAction(EAction.LuckOfTheMountaineer, EAction.LuckOfThePioneer))
                {
                    _usedLuck = true;
                    logger.LogDebug("Using Luck");
                    actions.Enqueue(PickAction(EAction.LuckOfTheMountaineer, EAction.LuckOfThePioneer));
                    return actions;
                }

                if (_usedLuck)
                {
                    // we still can't find the item, if this node has been hit at least once we just close it
                    logger.LogDebug("Didn't find item after using Luck, moving on...");
                    if (nodeCondition.CurrentIntegrity != nodeCondition.MaxIntegrity)
                        return null;

                    logger.LogDebug("Actually there's crystals, let's get those");
                    // otherwise, there most likely is -any- other item available, probably a shard/crystal
                    _slotToGather = slots.MinBy(x => x.ItemId);
                    return actions;
                }
            }

            slot = slots.SingleOrDefault(x => x.ItemId == Task.Request.ItemId);
            if (slot is { GatheringChance: > 0 and < 100 })
            {
                if (slot.GatheringChance >= 95 &&
                    CanUseAction(EAction.SharpVision1, EAction.FieldMastery1))
                {
                    logger.LogDebug("GatheringChance != 100, >= 95, using SharpVision1/FieldMastery1");
                    actions.Enqueue(PickAction(EAction.SharpVision1, EAction.FieldMastery1));
                    return actions;
                }

                if (slot.GatheringChance >= 85 &&
                    CanUseAction(EAction.SharpVision2, EAction.FieldMastery2))
                {
                    logger.LogDebug("GatheringChance != 100, >= 85, using SharpVision1/FieldMastery1");
                    actions.Enqueue(PickAction(EAction.SharpVision2, EAction.FieldMastery2));
                    return actions;
                }

                if (slot.GatheringChance >= 50 &&
                    CanUseAction(EAction.SharpVision3, EAction.FieldMastery3))
                {
                    logger.LogDebug("GatheringChance != 100, >= 50, using SharpVision1/FieldMastery1");
                    actions.Enqueue(PickAction(EAction.SharpVision3, EAction.FieldMastery3));
                    return actions;
                }
            }
            //}

            return actions;
        }

        private unsafe EAction PickAction(EAction minerAction, EAction botanistAction)
        {
            if ((Job?)PlayerState.Instance()->CurrentClassJobId == Job.MIN)
                return minerAction;

            return botanistAction;
        }

        private unsafe bool CanUseAction(EAction minerAction, EAction botanistAction)
        {
            EAction action = PickAction(minerAction, botanistAction);
            return ActionManager.Instance()->GetActionStatus(ActionType.Action, (uint)action) == 0;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    private sealed record SlotInfo(int Index, uint ItemId, int GatheringChance, int BoonChance, int Quantity);

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    private sealed record NodeCondition
    (
        uint CurrentIntegrity,
        uint MaxIntegrity);
}
