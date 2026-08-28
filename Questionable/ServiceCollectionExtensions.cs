using JetBrains.Annotations;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Gathering;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;

namespace Questionable;

internal static class ServiceCollectionExtensions
{
    public static void AddTaskFactory<
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    TFactory>(
            this IServiceCollection serviceCollection)
    where TFactory : class, ITaskFactory
    {
        serviceCollection.AddSingleton<ITaskFactory, TFactory>();
        serviceCollection.AddSingleton<TFactory>();
    }

    public static void AddTaskExecutor<T,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    TExecutor>(
            this IServiceCollection serviceCollection)
    where T : class, ITask
    where TExecutor : TaskExecutor<T>
    {
        serviceCollection.AddKeyedTransient<ITaskExecutor, TExecutor>(typeof(T));
        serviceCollection.AddTransient<TExecutor>();
    }

    public static void AddTaskFactoryAndExecutor<T,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    TFactory,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    TExecutor>(
            this IServiceCollection serviceCollection)
    where TFactory : class, ITaskFactory
    where T : class, ITask
    where TExecutor : TaskExecutor<T>
    {
        serviceCollection.AddTaskFactory<TFactory>();
        serviceCollection.AddTaskExecutor<T, TExecutor>();
    }

    /// <summary>
    /// Task factories and executors need paired registrations (ITaskFactory + self, or
    /// keyed ITaskExecutor + self), which don't map cleanly onto a single Injectio attribute.
    /// This module keeps the pairing helpers above and is picked up automatically by
    /// Injectio's generated <c>AddQuestionable</c> extension.
    /// </summary>
    [RegisterServices]
    public static void AddTaskRegistrations(IServiceCollection serviceCollection)
    {
        // individual tasks
        serviceCollection.AddTaskFactory<QuestCleanUp.CheckAlliedSocietyMount>();
        serviceCollection.AddTaskFactoryAndExecutor<QuestCleanUp.CloseGatheringAddonTask, QuestCleanUp.CloseGatheringAddonFactory, QuestCleanUp.DoCloseAddon>();
        serviceCollection.AddTaskExecutor<AbandonQuest.Task, AbandonQuest.AbandonQuestExecutor>();
        serviceCollection.AddTaskExecutor<LogQuestCompletion.Task, LogQuestCompletion.LogQuestCompletionExecutor>();
        serviceCollection
            .AddTaskExecutor<MoveToLandingLocation.Task, MoveToLandingLocation.MoveToLandingLocationExecutor>();
        //serviceCollection.AddTaskFactoryAndExecutor<Mail.ClaimMailTask, Mail.Factory, Mail.ClaimMailExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<SkipCondition.SkipTask, SkipCondition.Factory, SkipCondition.CheckSkip>();
        serviceCollection
            .AddTaskFactoryAndExecutor<RedeemRewardItems.Task, RedeemRewardItems.Factory, RedeemRewardItems.Executor>();
        serviceCollection.AddTaskExecutor<DoGather.Task, DoGather.GatherExecutor>();
        serviceCollection.AddTaskExecutor<DoGatherCollectable.Task, DoGatherCollectable.GatherCollectableExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<SwitchClassJob.Task, SwitchClassJob.Factory,
            SwitchClassJob.SwitchClassJobExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<CreateGearset.Task, CreateGearset.Factory,
            CreateGearset.CreateGearsetExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<UpdateGearset.Task, UpdateGearset.Factory,
            UpdateGearset.UpdateGearsetExecutor>();
        serviceCollection.AddTaskExecutor<MountStep.MountTask, MountStep.MountExecutor>();
        serviceCollection.AddTaskExecutor<MountStep.UnmountTask, MountStep.UnmountExecutor>();

        // task factories
        serviceCollection
            .AddTaskFactoryAndExecutor<StepDisabled.SkipRemainingTasks, StepDisabled.Factory,
                StepDisabled.SkipDisabledStepsExecutor>();
        serviceCollection.AddTaskFactory<EquipRecommended.BeforeDutyOrInstance>();
        serviceCollection.AddTaskExecutor<Gather.SkipMarker, Gather.DoSkip>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetheryteShortcut.Task, AetheryteShortcut.Factory,
                AetheryteShortcut.UseAetheryteShortcut>();
        serviceCollection
            .AddTaskExecutor<AetheryteShortcut.MoveAwayFromAetheryte,
                AetheryteShortcut.MoveAwayFromAetheryteExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Gather.GatheringTask, Gather.Factory, Gather.StartGathering>();
        serviceCollection.AddTaskExecutor<Gather.DelayedGatheringTask, Gather.DelayedGatheringExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AethernetShortcut.Task, AethernetShortcut.Factory,
                AethernetShortcut.UseAethernetShortcut>();
        serviceCollection
            .AddTaskFactoryAndExecutor<WaitAtStart.WaitDelay, WaitAtStart.Factory, WaitAtStart.WaitDelayExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<MoveTask, MoveTo.Factory, MoveExecutor>();
        serviceCollection.AddTaskExecutor<WaitForNearDataId, WaitForNearDataIdExecutor>();
        serviceCollection.AddTaskExecutor<LandTask, LandExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<SendNotification.Task, SendNotification.Factory, SendNotification.Executor>();

        serviceCollection
            .AddTaskFactoryAndExecutor<NextQuest.SetQuestTask, NextQuest.Factory, NextQuest.NextQuestExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetherCurrent.Attune, AetherCurrent.Factory, AetherCurrent.DoAttune>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AethernetShard.Attune, AethernetShard.Factory, AethernetShard.DoAttune>();
        serviceCollection.AddTaskFactoryAndExecutor<Aetheryte.Attune, Aetheryte.Factory, Aetheryte.DoAttune>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetheryteFreeOrFavored.Register, AetheryteFreeOrFavored.Factory,
                AetheryteFreeOrFavored.DoRegister>();
        serviceCollection.AddTaskFactoryAndExecutor<Combat.Task, Combat.Factory, Combat.HandleCombat>();
        serviceCollection
            .AddTaskFactoryAndExecutor<Duty.OpenDutyFinderTask, Duty.Factory, Duty.OpenDutyFinderExecutor>();
        serviceCollection.AddTaskExecutor<Duty.StartAutoDutyTask, Duty.StartAutoDutyExecutor>();
        serviceCollection.AddTaskExecutor<Duty.WaitAutoDutyTask, Duty.WaitAutoDutyExecutor>();
        serviceCollection.AddTaskFactory<Emote.Factory>();
        serviceCollection.AddTaskExecutor<Emote.UseOnObject, Emote.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<Emote.UseOnSelf, Emote.UseOnSelfExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<ActionStep.UseOnObject, ActionStep.Factory, ActionStep.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<ActionStep.UseMudraOnObject, ActionStep.UseMudraOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<ActionStep.TriggerStatusIfMissing, ActionStep.TriggerStatusIfMissingExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<StatusOff.Task, StatusOff.Factory, StatusOff.DoStatusOff>();
        serviceCollection.AddTaskFactoryAndExecutor<Interact.Task, Interact.Factory, Interact.DoInteract>();
        serviceCollection.AddTaskFactory<Jump.Factory>();
        serviceCollection.AddTaskExecutor<Jump.SingleJumpTask, Jump.DoSingleJump>();
        serviceCollection.AddTaskExecutor<Jump.RepeatedJumpTask, Jump.DoRepeatedJumps>();
        serviceCollection.AddTaskFactoryAndExecutor<Dive.Task, Dive.Factory, Dive.DoDive>();
        serviceCollection.AddTaskFactoryAndExecutor<Say.Task, Say.Factory, Say.UseChat>();
        serviceCollection.AddTaskFactory<UseItem.Factory>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnGround, UseItem.UseOnGroundExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnPosition, UseItem.UseOnPositionExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnObject, UseItem.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnSelf, UseItem.UseOnSelfExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<EquipItem.Task, EquipItem.Factory, EquipItem.DoEquip>();
        serviceCollection.AddTaskFactoryAndExecutor<UnequipItem.Task, UnequipItem.Factory, UnequipItem.DoUnequip>();
        serviceCollection
            .AddTaskFactoryAndExecutor<EquipRecommended.EquipTask, EquipRecommended.Factory,
                EquipRecommended.DoEquipRecommended>();
        serviceCollection.AddTaskFactoryAndExecutor<Craft.CraftTask, Craft.Factory, Craft.DoCraft>();
        serviceCollection.AddTaskFactoryAndExecutor<Fish.FishTask, Fish.Factory, Fish.DoFish>();
        serviceCollection
            .AddTaskFactoryAndExecutor<TurnInDelivery.Task, TurnInDelivery.Factory,
                TurnInDelivery.SatisfactionSupplyTurnIn>();

        serviceCollection.AddTaskFactory<SinglePlayerDuty.Factory>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.LeaveParty, SinglePlayerDuty.LeavePartyExecutor>();
        serviceCollection
            .AddTaskExecutor<SinglePlayerDuty.StartSinglePlayerDuty, SinglePlayerDuty.StartSinglePlayerDutyExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.EnableAi, SinglePlayerDuty.EnableAiExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.SetPreset, SinglePlayerDuty.SetPresetExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.Commence, SinglePlayerDuty.CommenceExecutor>();
        serviceCollection
            .AddTaskExecutor<SinglePlayerDuty.WaitSinglePlayerDuty, SinglePlayerDuty.WaitSinglePlayerDutyExecutor>();
        serviceCollection
            .AddTaskExecutor<SinglePlayerDuty.WaitForSinglePlayerDutyOutcome,
                SinglePlayerDuty.WaitForSinglePlayerDutyOutcomeExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.DisableAi, SinglePlayerDuty.DisableAiExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.SetTarget, SinglePlayerDuty.SetTargetExecutor>();

        serviceCollection.AddTaskExecutor<WaitCondition.Task, WaitCondition.WaitConditionExecutor>();
        serviceCollection.AddTaskExecutor<WaitNavmesh.Task, WaitNavmesh.Executor>();
        serviceCollection.AddTaskFactory<WaitAtEnd.Factory>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitDelay, WaitAtEnd.WaitDelayExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitNextStepOrSequence, WaitAtEnd.WaitNextStepOrSequenceExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitForCompletionFlags, WaitAtEnd.WaitForCompletionFlagsExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitObjectAtPosition, WaitAtEnd.WaitObjectAtPositionExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitQuestAccepted, WaitAtEnd.WaitQuestAcceptedExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitQuestCompleted, WaitAtEnd.WaitQuestCompletedExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitForTerritory, WaitAtEnd.WaitForTerritoryExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.NextStep, WaitAtEnd.NextStepExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.EndAutomation, WaitAtEnd.EndAutomationExecutor>();
    }
}
