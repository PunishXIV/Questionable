using JetBrains.Annotations;
using Questionable.Controller.Steps.Gathering;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using AethernetShortcut = Questionable.Controller.Steps.Shared.AethernetShortcut;

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

    [RegisterServices]
    public static void AddTaskRegistrations(IServiceCollection serviceCollection)
    {
        serviceCollection.AddTaskFactory<QuestCleanUp.CheckAlliedSocietyMount>();
        serviceCollection.AddTaskFactoryAndExecutor<QuestCleanUp.CloseAddonTask, QuestCleanUp.CloseAddonFactory, QuestCleanUp.CloseAddonExecutor>();
        serviceCollection.AddTaskExecutor<AbandonQuest.Task, AbandonQuest.Executor>();
        serviceCollection.AddTaskExecutor<LogQuestCompletion.Task, LogQuestCompletion.Executor>();
        serviceCollection.AddTaskFactory<CleanUpOtherQuests.Factory>();
        serviceCollection
            .AddTaskFactoryAndExecutor<SkipCondition.Task, SkipCondition.Factory, SkipCondition.Executor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<RedeemRewardItems.Task, RedeemRewardItems.Factory, RedeemRewardItems.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<SwitchClassJob.Task, SwitchClassJob.Factory,
            SwitchClassJob.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<CreateGearset.Task, CreateGearset.Factory,
            CreateGearset.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<UpdateGearset.Task, UpdateGearset.Factory,
            UpdateGearset.Executor>();
        serviceCollection.AddTaskExecutor<MountStep.MountTask, MountStep.MountExecutor>();
        serviceCollection.AddTaskExecutor<MountStep.UnmountTask, MountStep.UnmountExecutor>();

        serviceCollection
            .AddTaskFactoryAndExecutor<StepDisabled.Task, StepDisabled.Factory,
                StepDisabled.Executor>();
        serviceCollection.AddTaskFactory<EquipRecommended.BeforeDutyOrInstance>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetheryteShortcut.Task, AetheryteShortcut.Factory,
                AetheryteShortcut.Executor>();
        serviceCollection
            .AddTaskExecutor<AetheryteShortcut.MoveAway,
                AetheryteShortcut.MoveAwayExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Gather.Task, Gather.Factory, Gather.Executor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AethernetShortcut.Task, AethernetShortcut.Factory,
                AethernetShortcut.Executor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<WaitAtStart.WaitDelay, WaitAtStart.Factory, WaitAtStart.WaitDelayExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<MoveTask, MoveTo.Factory, MoveExecutor>();
        serviceCollection.AddTaskExecutor<WaitForNearDataId, WaitForNearDataIdExecutor>();
        serviceCollection.AddTaskExecutor<LandTask, LandExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<SendNotification.Task, SendNotification.Factory, SendNotification.Executor>();

        serviceCollection
            .AddTaskFactoryAndExecutor<NextQuest.SetQuestTask, NextQuest.Factory, NextQuest.SetQuestExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetherCurrent.Attune, AetherCurrent.Factory, AetherCurrent.AttuneExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AethernetShard.Attune, AethernetShard.Factory, AethernetShard.AttuneExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Aetheryte.Attune, Aetheryte.Factory, Aetheryte.AttuneExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<AetheryteFreeOrFavored.Register, AetheryteFreeOrFavored.Factory,
                AetheryteFreeOrFavored.RegisterExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Combat.Task, Combat.Factory, Combat.Executor>();
        serviceCollection.AddTaskExecutor<Combat.CheckAfterInterruption, Combat.CheckAfterInterruptionExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<Duty.OpenDutyFinderTask, Duty.Factory, Duty.OpenDutyFinderExecutor>();
        serviceCollection.AddTaskExecutor<Duty.StartAutoDutyTask, Duty.StartAutoDutyExecutor>();
        serviceCollection.AddTaskExecutor<Duty.OpenVariantDFTask, Duty.OpenVariantDFExecutor>();
        serviceCollection.AddTaskFactory<Emote.Factory>();
        serviceCollection.AddTaskExecutor<Emote.UseOnObject, Emote.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<Emote.UseOnSelf, Emote.UseOnSelfExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<ActionStep.UseOnObject, ActionStep.Factory, ActionStep.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<ActionStep.UseMudraOnObject, ActionStep.UseMudraOnObjectExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<StatusOff.Task, StatusOff.Factory, StatusOff.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<Interact.Task, Interact.Factory, Interact.Executor>();
        serviceCollection.AddTaskFactory<Jump.Factory>();
        serviceCollection.AddTaskExecutor<Jump.SingleTask, Jump.SingleExecutor>();
        serviceCollection.AddTaskExecutor<Jump.RepeatedTask, Jump.RepeatedExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Dive.Task, Dive.Factory, Dive.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<Say.Task, Say.Factory, Say.Executor>();
        serviceCollection.AddTaskFactory<UseItem.Factory>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnGround, UseItem.UseOnGroundExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnPosition, UseItem.UseOnPositionExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnObject, UseItem.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<UseItem.UseOnSelf, UseItem.UseOnSelfExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<EquipItem.Task, EquipItem.Factory, EquipItem.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<UnequipItem.Task, UnequipItem.Factory, UnequipItem.Executor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<EquipRecommended.EquipTask, EquipRecommended.Factory,
                EquipRecommended.EquipExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Craft.Task, Craft.Factory, Craft.Executor>();
        serviceCollection.AddTaskFactoryAndExecutor<Purchase.BuyTask, Purchase.Factory, Purchase.BuyExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<Fish.Task, Fish.Factory, Fish.Executor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<TurnInDelivery.Task, TurnInDelivery.Factory,
                TurnInDelivery.Executor>();

        serviceCollection.AddSingleton<QuestBattle.RetryCounter>();
        serviceCollection.AddTaskFactory<QuestBattle.Factory>();
        serviceCollection.AddTaskExecutor<QuestBattle.LeaveParty, QuestBattle.LeavePartyExecutor>();
        serviceCollection
            .AddTaskExecutor<QuestBattle.Start, QuestBattle.StartExecutor>();
        serviceCollection.AddTaskExecutor<QuestBattle.EnableAi, QuestBattle.EnableAiExecutor>();
        serviceCollection
            .AddTaskExecutor<QuestBattle.WaitForExit, QuestBattle.WaitForExitExecutor>();
        serviceCollection
            .AddTaskExecutor<QuestBattle.WaitForOutcome,
                QuestBattle.WaitForOutcomeExecutor>();
        serviceCollection.AddTaskExecutor<QuestBattle.DisableAi, QuestBattle.DisableAiExecutor>();

        serviceCollection.AddTaskExecutor<WaitCondition.Task, WaitCondition.Executor>();
        serviceCollection.AddTaskExecutor<WaitNavmesh.Task, WaitNavmesh.Executor>();
        serviceCollection.AddTaskFactory<WaitAtEnd.Factory>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.WaitDelay, WaitAtEnd.WaitDelayExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.NextStepOrSequence, WaitAtEnd.NextStepOrSequenceExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.CompletionFlags, WaitAtEnd.CompletionFlagsExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.ObjectAtPosition, WaitAtEnd.ObjectAtPositionExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.QuestAccepted, WaitAtEnd.QuestAcceptedExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.QuestCompleted, WaitAtEnd.QuestCompletedExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.TerritoryEntered, WaitAtEnd.TerritoryEnteredExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.NextStep, WaitAtEnd.NextStepExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.EndAutomation, WaitAtEnd.EndAutomationExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.ConditionActive, WaitAtEnd.ConditionActiveExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.ConditionCleared, WaitAtEnd.ConditionClearedExecutor>();
    }
}
