using System;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Extensions.MicrosoftLogging;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LLib;
using LLib.Gear;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.GameUi;
using Questionable.Controller.NavigationOverrides;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Gathering;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Questionable.Windows;
using Questionable.Windows.ConfigComponents;
using Questionable.Windows.JournalComponents;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
using Action = Questionable.Controller.Steps.Interactions.Action;

namespace Questionable;

[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed class QuestionablePlugin : IDalamudPlugin
{
    private readonly ServiceProvider? _serviceProvider;

    public QuestionablePlugin(IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        ITargetManager targetManager,
        IFramework framework,
        IGameGui gameGui,
        IDataManager dataManager,
        ISigScanner sigScanner,
        IObjectTable objectTable,
        IPluginLog pluginLog,
        ICondition condition,
        IChatGui chatGui,
        ICommandManager commandManager,
        IAddonLifecycle addonLifecycle,
        IKeyState keyState,
        IContextMenu contextMenu,
        IToastGui toastGui,
        IGameInteropProvider gameInteropProvider,
        IAetheryteList aetheryteList)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(chatGui);

        try
        {
            ServiceCollection serviceCollection = new();
            serviceCollection.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
                .ClearProviders()
                .AddDalamudLogger(pluginLog, t => t[(t.LastIndexOf('.') + 1)..]));
            serviceCollection.AddSingleton<IDalamudPlugin>(this);
            serviceCollection.AddSingleton(pluginInterface);
            serviceCollection.AddSingleton(clientState);
            serviceCollection.AddSingleton(targetManager);
            serviceCollection.AddSingleton(framework);
            serviceCollection.AddSingleton(gameGui);
            serviceCollection.AddSingleton(dataManager);
            serviceCollection.AddSingleton(sigScanner);
            serviceCollection.AddSingleton(objectTable);
            serviceCollection.AddSingleton(pluginLog);
            serviceCollection.AddSingleton(condition);
            serviceCollection.AddSingleton(chatGui);
            serviceCollection.AddSingleton(commandManager);
            serviceCollection.AddSingleton(addonLifecycle);
            serviceCollection.AddSingleton(keyState);
            serviceCollection.AddSingleton(contextMenu);
            serviceCollection.AddSingleton(toastGui);
            serviceCollection.AddSingleton(gameInteropProvider);
            serviceCollection.AddSingleton(aetheryteList);
            serviceCollection.AddSingleton(new WindowSystem(nameof(Questionable)));
            serviceCollection.AddSingleton((Configuration?)pluginInterface.GetPluginConfig() ?? new Configuration());

            AddBasicFunctionsAndData(serviceCollection);
            AddTaskFactories(serviceCollection);
            AddControllers(serviceCollection);
            AddWindows(serviceCollection);
            AddQuestValidators(serviceCollection);

            serviceCollection.AddSingleton<CommandHandler>();
            serviceCollection.AddSingleton<DalamudInitializer>();

            _serviceProvider = serviceCollection.BuildServiceProvider();
            Initialize(_serviceProvider);
        }
        catch (Exception)
        {
            chatGui.PrintError("Unable to load plugin, check /xllog for details", "Questionable");
            throw;
        }
    }

    private static void AddBasicFunctionsAndData(ServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<AetheryteFunctions>();
        serviceCollection.AddSingleton<ExcelFunctions>();
        serviceCollection.AddSingleton<GameFunctions>();
        serviceCollection.AddSingleton<ChatFunctions>();
        serviceCollection.AddSingleton<QuestFunctions>();
        serviceCollection.AddSingleton<AlliedSocietyQuestFunctions>();
        serviceCollection.AddSingleton<DalamudReflector>();
        serviceCollection.AddSingleton<Mount.MountEvaluator>();

        serviceCollection.AddSingleton<AetherCurrentData>();
        serviceCollection.AddSingleton<AetheryteData>();
        serviceCollection.AddSingleton<AlliedSocietyData>();
        serviceCollection.AddSingleton<GatheringData>();
        serviceCollection.AddSingleton<JournalData>();
        serviceCollection.AddSingleton<QuestData>();
        serviceCollection.AddSingleton<TerritoryData>();
        serviceCollection.AddSingleton<NavmeshIpc>();
        serviceCollection.AddSingleton<LifestreamIpc>();
        serviceCollection.AddSingleton<ArtisanIpc>();
        serviceCollection.AddSingleton<QuestionableIpc>();
        serviceCollection.AddSingleton<TextAdvanceIpc>();
        serviceCollection.AddSingleton<NotificationMasterIpc>();
        serviceCollection.AddSingleton<AutomatonIpc>();
        serviceCollection.AddSingleton<AutoDutyIpc>();
        serviceCollection.AddSingleton<BossModIpc>();
        serviceCollection.AddSingleton<PandorasBoxIpc>();

        serviceCollection.AddSingleton<GearStatsCalculator>();
    }

    private static void AddTaskFactories(ServiceCollection serviceCollection)
    {
        // individual tasks
        serviceCollection.AddTaskFactory<QuestCleanUp.CheckAlliedSocietyMount>();
        serviceCollection.AddTaskFactoryAndExecutor<QuestCleanUp.CloseGatheringAddonTask, QuestCleanUp.CloseGatheringAddonFactory, QuestCleanUp.DoCloseAddon>();
        serviceCollection
            .AddTaskExecutor<MoveToLandingLocation.Task, MoveToLandingLocation.MoveToLandingLocationExecutor>();
        serviceCollection
            .AddTaskFactoryAndExecutor<RedeemRewardItems.Task, RedeemRewardItems.Factory, RedeemRewardItems.Executor>();
        serviceCollection.AddTaskExecutor<DoGather.Task, DoGather.GatherExecutor>();
        serviceCollection.AddTaskExecutor<DoGatherCollectable.Task, DoGatherCollectable.GatherCollectableExecutor>();
        serviceCollection.AddTaskFactoryAndExecutor<SwitchClassJob.Task, SwitchClassJob.Factory,
            SwitchClassJob.SwitchClassJobExecutor>();
        serviceCollection.AddTaskExecutor<Mount.MountTask, Mount.MountExecutor>();
        serviceCollection.AddTaskExecutor<Mount.UnmountTask, Mount.UnmountExecutor>();

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
        serviceCollection
            .AddTaskFactoryAndExecutor<SkipCondition.SkipTask, SkipCondition.Factory, SkipCondition.CheckSkip>();
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
        serviceCollection.AddTaskFactoryAndExecutor<Action.UseOnObject, Action.Factory, Action.UseOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<Action.UseMudraOnObject, Action.UseMudraOnObjectExecutor>();
        serviceCollection.AddTaskExecutor<Action.TriggerStatusIfMissing, Action.TriggerStatusIfMissingExecutor>();
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
        serviceCollection
            .AddTaskFactoryAndExecutor<EquipRecommended.EquipTask, EquipRecommended.Factory,
                EquipRecommended.DoEquipRecommended>();
        serviceCollection.AddTaskFactoryAndExecutor<Craft.CraftTask, Craft.Factory, Craft.DoCraft>();
        serviceCollection
            .AddTaskFactoryAndExecutor<TurnInDelivery.Task, TurnInDelivery.Factory,
                TurnInDelivery.SatisfactionSupplyTurnIn>();

        serviceCollection.AddTaskFactory<SinglePlayerDuty.Factory>();
        serviceCollection
            .AddTaskExecutor<SinglePlayerDuty.StartSinglePlayerDuty, SinglePlayerDuty.StartSinglePlayerDutyExecutor>();
        serviceCollection.AddTaskExecutor<SinglePlayerDuty.EnableAi, SinglePlayerDuty.EnableAiExecutor>();
        serviceCollection
            .AddTaskExecutor<SinglePlayerDuty.WaitSinglePlayerDuty, SinglePlayerDuty.WaitSinglePlayerDutyExecutor>();
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
        serviceCollection.AddTaskExecutor<WaitAtEnd.NextStep, WaitAtEnd.NextStepExecutor>();
        serviceCollection.AddTaskExecutor<WaitAtEnd.EndAutomation, WaitAtEnd.EndAutomationExecutor>();

        serviceCollection.AddSingleton<TaskCreator>();
        serviceCollection.AddSingleton<ExtraConditionUtils>();
        serviceCollection.AddSingleton<ClassJobUtils>();
    }

    private static void AddControllers(ServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<MovementController>();
        serviceCollection.AddSingleton<MovementOverrideController>();
        serviceCollection.AddSingleton<GatheringPointRegistry>();
        serviceCollection.AddSingleton<QuestRegistry>();
        serviceCollection.AddSingleton<QuestController>();
        serviceCollection.AddSingleton<CombatController>();
        serviceCollection.AddSingleton<GatheringController>();
        serviceCollection.AddSingleton<ContextMenuController>();
        serviceCollection.AddSingleton<ShopController>();
        serviceCollection.AddSingleton<InterruptHandler>();

        serviceCollection.AddSingleton<PartyWatchDog>();

        serviceCollection.AddSingleton<CraftworksSupplyController>();
        serviceCollection.AddSingleton<CreditsController>();
        serviceCollection.AddSingleton<HelpUiController>();
        serviceCollection.AddSingleton<InteractionUiController>();

        serviceCollection.AddSingleton<ICombatModule, Mount128Module>();
        serviceCollection.AddSingleton<ICombatModule, Mount147Module>();
        serviceCollection.AddSingleton<ICombatModule, ItemUseModule>();
        serviceCollection.AddSingleton<ICombatModule, BossModModule>();
        serviceCollection.AddSingleton<ICombatModule, WrathComboModule>();
        serviceCollection.AddSingleton<ICombatModule, RotationSolverRebornModule>();
    }

    private static void AddWindows(ServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<UiUtils>();
        serviceCollection.AddTransient<QuestSelector>();

        serviceCollection.AddSingleton<ActiveQuestComponent>();
        serviceCollection.AddSingleton<ARealmRebornComponent>();
        serviceCollection.AddSingleton<CreationUtilsComponent>();
        serviceCollection.AddSingleton<EventInfoComponent>();
        serviceCollection.AddSingleton<QuestTooltipComponent>();
        serviceCollection.AddSingleton<QuickAccessButtonsComponent>();
        serviceCollection.AddSingleton<RemainingTasksComponent>();

        serviceCollection.AddSingleton<QuestJournalUtils>();
        serviceCollection.AddSingleton<QuestJournalComponent>();
        serviceCollection.AddSingleton<QuestRewardComponent>();
        serviceCollection.AddSingleton<GatheringJournalComponent>();
        serviceCollection.AddSingleton<AlliedSocietyJournalComponent>();

        serviceCollection.AddSingleton<OneTimeSetupWindow>();
        serviceCollection.AddSingleton<QuestWindow>();
        serviceCollection.AddSingleton<ConfigWindow>();
        serviceCollection.AddSingleton<DebugOverlay>();
        serviceCollection.AddSingleton<QuestSelectionWindow>();
        serviceCollection.AddSingleton<QuestValidationWindow>();
        serviceCollection.AddSingleton<JournalProgressWindow>();
        serviceCollection.AddSingleton<PriorityWindow>();

        serviceCollection.AddSingleton<GeneralConfigComponent>();
        serviceCollection.AddSingleton<PluginConfigComponent>();
        serviceCollection.AddSingleton<DutyConfigComponent>();
        serviceCollection.AddSingleton<SinglePlayerDutyConfigComponent>();
        serviceCollection.AddSingleton<StopConditionComponent>();
        serviceCollection.AddSingleton<NotificationConfigComponent>();
        serviceCollection.AddSingleton<DebugConfigComponent>();
        serviceCollection.AddSingleton<SupportConfigComponent>();
    }

    private static void AddQuestValidators(ServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<QuestValidator>();
        serviceCollection.AddSingleton<IQuestValidator, QuestDisabledValidator>();
        serviceCollection.AddSingleton<IQuestValidator, BasicSequenceValidator>();
        serviceCollection.AddSingleton<IQuestValidator, UniqueStartStopValidator>();
        serviceCollection.AddSingleton<IQuestValidator, NextQuestValidator>();
        serviceCollection.AddSingleton<IQuestValidator, CompletionFlagsValidator>();
        serviceCollection.AddSingleton<IQuestValidator, AethernetShortcutValidator>();
        serviceCollection.AddSingleton<IQuestValidator, DialogueChoiceValidator>();
        serviceCollection.AddSingleton<IQuestValidator, ClassQuestShouldHaveShortcutValidator>();
        serviceCollection.AddSingleton<IQuestValidator, SinglePlayerInstanceValidator>();
        serviceCollection.AddSingleton<IQuestValidator, UniqueSinglePlayerInstanceValidator>();
        serviceCollection.AddSingleton<IQuestValidator, SayValidator>();
        serviceCollection.AddSingleton<JsonSchemaValidator>();
        serviceCollection.AddSingleton<IQuestValidator>(sp => sp.GetRequiredService<JsonSchemaValidator>());
    }

    private static void Initialize(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<QuestRegistry>().Reload();
        serviceProvider.GetRequiredService<GatheringPointRegistry>().Reload();
        serviceProvider.GetRequiredService<SinglePlayerDutyConfigComponent>().Reload();
        serviceProvider.GetRequiredService<SupportConfigComponent>();
        serviceProvider.GetRequiredService<CommandHandler>();
        serviceProvider.GetRequiredService<ContextMenuController>();
        serviceProvider.GetRequiredService<CraftworksSupplyController>();
        serviceProvider.GetRequiredService<CreditsController>();
        serviceProvider.GetRequiredService<HelpUiController>();
        serviceProvider.GetRequiredService<ShopController>();
        serviceProvider.GetRequiredService<QuestionableIpc>();
        serviceProvider.GetRequiredService<DalamudInitializer>();
        serviceProvider.GetRequiredService<TextAdvanceIpc>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
