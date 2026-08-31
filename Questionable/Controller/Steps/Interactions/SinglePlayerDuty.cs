using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Interactions;

internal static class SinglePlayerDuty
{
    internal const int MaxRetries = 5;

    private static (ElementId QuestId, uint CfcId, byte Sequence)? _lastRetryKey;
    private static int _retryCount;

    private static int RecordRetry(ElementId questId, uint cfcId, byte sequence)
    {
        var key = (questId, cfcId, sequence);
        if (_lastRetryKey != key)
        {
            _lastRetryKey = key;
            _retryCount = 0;
        }

        return ++_retryCount;
    }

    private static void ClearRetryCount()
    {
        _lastRetryKey = null;
        _retryCount = 0;
    }

    internal static class SpecialTerritories
    {
        public const ushort Lahabrea = 1052;
        public const ushort ItsProbablyATrap = 665;
        public const ushort Naadam = 688;
        public const ushort Patisserie = 1298;
        public const ushort ViperTutorial = 1235;
        public const ushort EgistentialCrisis = 701;
        public const ushort Nightkin = 676;
        public const ushort WarmthOfFamily = 1244;
        public const ushort BarThePassage = 1246;
    }

    internal sealed class Factory
    (
        BossModIpc bossModIpc,
        TerritoryData territoryData,
        IObjectTable objectTable,
        ICondition condition,
        IClientState clientState) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SinglePlayerDuty)
                yield break;

            if (bossModIpc.IsConfiguredToRunSoloInstance(quest.Id, step.SinglePlayerDutyOptions))
            {
                uint cfcId = 0;
                uint tId = 0;
                TerritoryData.ContentFinderConditionData? cfcData = null;
                if (quest.Id.Value.Equals(5325))
                {
                    cfcId = 1045;
                    tId = 1298;
                }
                else if (!territoryData.TryGetContentFinderConditionForSoloInstance(quest.Id, step.SinglePlayerDutyIndex, out cfcData))
                    throw new TaskException("Failed to get content finder condition for solo instance");

                if (cfcData != null)
                {
                    cfcId = cfcData.ContentFinderConditionId;
                    tId = cfcData.TerritoryId;
                }

                yield return new MountStep.UnmountTask();
                //if (ShouldLeaveParty || condition[ConditionFlag.ParticipatingInCrossWorldPartyOrAlliance])
                //{
                //    yield return new LeaveParty();
                //    yield return new WaitAtStart.WaitDelay(TimeSpan.FromSeconds(0.5));
                //}
                if (tId == SpecialTerritories.Patisserie)
                    yield return new Commence(cfcId);
                yield return new StartSinglePlayerDuty(cfcId);
                yield return new WaitAtStart.WaitDelay(TimeSpan.FromSeconds(2)); // maybe a delay will work here too, needs investigation
                if (tId == SpecialTerritories.Lahabrea)
                {
                    yield return new SetTarget(14643);
                    yield return new EnableAi();
                    yield return new WaitCondition.Task(
                        () => condition[ConditionFlag.Unconscious] || clientState.TerritoryType != SpecialTerritories.Lahabrea,
                        "Wait(death)");
                    yield return new DisableAi();
                    yield return new WaitCondition.Task(
                        () => !condition[ConditionFlag.Unconscious] || clientState.TerritoryType != SpecialTerritories.Lahabrea,
                        "Wait(resurrection)");
                    yield return new EnableAi();
                }
                else if (tId is SpecialTerritories.ItsProbablyATrap)
                {
                    yield return new EnableAi();
                    yield return new WaitCondition.Task(() => DutyActionsAvailable() || clientState.TerritoryType != SpecialTerritories.ItsProbablyATrap,
                        "Wait(Phase 2)");
                    yield return new DisableAi();
                    yield return new EnableAi(Passive: true);
                }
                else if (tId is SpecialTerritories.Naadam)
                {
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.Naadam)
                                return true;

                            Vector3 pos = objectTable[0]?.Position ?? default;
                            return (new Vector3(352.01f, -1.45f, 288.59f) - pos).Length() < 10f;
                        },
                        "Wait(moving to Ovoo)");
                    yield return new MountStep.UnmountTask();
                    yield return new EnableAi();
                }
                else if (tId == SpecialTerritories.Patisserie)
                    yield return new SetPreset(BossModIpc.EPreset.NormalMovement);
                else if (tId == SpecialTerritories.EgistentialCrisis)
                {
                    yield return new EnableAi();

                    yield return new MoveTask(SpecialTerritories.EgistentialCrisis, new(-173.63245f, 23.428497f, 67.91785f));
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.EgistentialCrisis)
                                return true;
                            return !condition[ConditionFlag.InCombat];
                        },
                        "Wait(finishing combat)");

                    yield return new MoveTask(SpecialTerritories.EgistentialCrisis, new(-81.944534f, 17.852749f, 28.01129f));
                    yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(10));

                    yield return new MoveTask(SpecialTerritories.EgistentialCrisis, new(-3.189148f, 15.807038f, -2.7008667f));
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.EgistentialCrisis)
                                return true;
                            return !condition[ConditionFlag.InCombat];
                        },
                        "Wait(finishing combat)");
                    yield return new MoveTask(SpecialTerritories.EgistentialCrisis, new(54.61206f, 11.988899f, 0.56451416f));
                    yield return new SetTarget(7256);
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.EgistentialCrisis)
                                return true;
                            return !condition[ConditionFlag.InCombat];
                        },
                        "Wait(finishing combat)");
                }
                else if (tId == SpecialTerritories.Nightkin)
                {
                    yield return new EnableAi();

                    foreach (Vector3 pos in new Vector3[] {
                        new(296.57343f, -23.999998f, -46.911057f),
                        new(252.63516f, -24f, -30.802874f),
                        new(292.96347f, -40.000004f, -58.68649f)
                    })
                    {
                        yield return new MoveTask(SpecialTerritories.Nightkin, pos, Mount: false);
                        yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.Nightkin)
                                return true;
                            return !condition[ConditionFlag.InCombat];
                        },
                        "Wait(finishing combat)");
                    }

                    foreach (Vector3 pos in new Vector3[] {
                        new(368.9174f, -39.0174f, -67.61273f),
                        new(364.1259f, -39.0174f, -61.63129f),
                        new(376.7352f, -39f, -72.19662f),
                        new(376.9069f, -39f, -47.19531f),
                        new(384.6841f, -39f, -51.21257f)
                    })
                    {
                        yield return new MoveTask(SpecialTerritories.Nightkin, pos, Mount: false);
                        yield return new Interact.Task(2007939, quest, EInteractionType.Interact, SkipMarkerCheck: true);
                    }
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.Nightkin)
                                return true;
                            return objectTable.Any(o => o.BaseId.Equals(7075));
                        },
                        "Wait(voidsphere spawn)");
                    yield return new MoveTask(SpecialTerritories.Nightkin, new(368.9174f, -39.0174f, -67.61273f), Mount: false);
                    yield return new WaitCondition.Task(
                    () =>
                    {
                        if (clientState.TerritoryType != SpecialTerritories.Nightkin)
                            return true;
                        return !condition[ConditionFlag.InCombat];
                    },
                    "Wait(finishing combat)");
                    yield return new MoveTask(SpecialTerritories.Nightkin, new(376.36014f, -39f, -60.74992f), Mount: false);
                    yield return new Interact.Task(2007939, quest, EInteractionType.Interact, SkipMarkerCheck: true);
                }

                else if (tId == SpecialTerritories.WarmthOfFamily)
                {
                    yield return new MoveTask(SpecialTerritories.WarmthOfFamily, new(328.9248f, -16.66298f, -310.9581f));
                    yield return new Interact.Task(2014395, quest, EInteractionType.Interact, SkipMarkerCheck: true);
                    yield return new MoveTask(SpecialTerritories.WarmthOfFamily, new(336.6056f, -16.60183f, -300.5593f));
                    yield return new Interact.Task(2014403, quest, EInteractionType.Interact, SkipMarkerCheck: true);
                    yield return new MoveTask(SpecialTerritories.WarmthOfFamily, new(414.32892f, -16.524708f, -127.3352f));
                    yield return new EnableAi();
                    yield return new WaitCondition.Task(
                    () =>
                    {
                        if (clientState.TerritoryType != SpecialTerritories.WarmthOfFamily)
                            return true;
                        return condition[ConditionFlag.SufferingStatusAffliction63];
                    },
                    "Wait(phase 2)");
                    yield return new WaitCondition.Task(
                    () =>
                    {
                        if (clientState.TerritoryType != SpecialTerritories.WarmthOfFamily)
                            return true;
                        return !condition[ConditionFlag.SufferingStatusAffliction63];
                    },
                    "Wait(in event)");
                    yield return new SetTarget(17976);
                    yield return new WaitCondition.Task(
                        () =>
                        {
                            if (clientState.TerritoryType != SpecialTerritories.WarmthOfFamily)
                                return true;
                            return objectTable.Any(o => o.BaseId.Equals(17992));
                        },
                        "Wait(leg exposed)");
                    yield return new SetTarget(17992);
                }
                else if (tId == SpecialTerritories.BarThePassage)
                {
                    yield return new EnableAi();
                    yield return new WaitCondition.Task(
                    () =>
                    {
                        if (clientState.TerritoryType != SpecialTerritories.BarThePassage)
                            return true;
                        return !condition[ConditionFlag.SufferingStatusAffliction63];
                    },
                    "Wait(in event)");
                    Vector3[] points = [
                            new(0f, 0f, -300f),
                            new(0f, 0f, -300f),
                            new(0f, 0f, -270f),
                            new(0f, 0f, 78f),
                            new(0f, 0f, 103f),
                            new(0f, 0f, 361f),
                    ];
                    foreach (Vector3 point in points)
                    {
                        yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(2));
                        yield return new WaitCondition.Task(
                            () =>
                            {
                                if (clientState.TerritoryType != SpecialTerritories.BarThePassage)
                                    return true;
                                return !condition[ConditionFlag.InCombat];
                            },
                            "Wait(in combat)");
                        yield return new MoveTask(SpecialTerritories.BarThePassage, point);
                    }
                    yield return new SetTarget(18032);
                }

                //else if (tId == SpecialTerritories.ViperTutorial)
                //{
                //    yield return new WaitCondition.Task(
                //    () =>
                //    {
                //        if (clientState.TerritoryType != SpecialTerritories.ViperTutorial)
                //            return true;
                //        return !condition[ConditionFlag.SufferingStatusAffliction63];
                //    },
                //    "Wait(in event)");
                //    yield return new Interact.Task(17055, quest, EInteractionType.Interact, SkipMarkerCheck: true);
                //    yield return new SetTarget(17057);
                //    yield return new MoveTask(SpecialTerritories.ViperTutorial, new(255.8717f, 15.075876f, 471.1717f), Mount: false);
                //    IEnumerable<EAction> actions = [
                //        EAction.WrithingSnap,
                //        EAction.SteelFangs,
                //        EAction.WrithingSnap,
                //        EAction.HuntersSting,
                //        EAction.WrithingSnap,
                //        EAction.FlankstingStrike,
                //        EAction.WrithingSnap,
                //        EAction.DeathRattle
                //    ];
                //    foreach (EAction action in actions)
                //    {
                //        yield return new SetTarget(17057);
                //        yield return new ActionStep.UseOnObject(17057, quest, action, CompletionQuestVariablesFlags: null);
                //        yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(2));
                //    }
                //    yield return new MoveTask(SpecialTerritories.ViperTutorial, new(251.17833f, 15.400139f, 465.84366f), Mount: false);
                //}
                else
                    yield return new EnableAi(tId == SpecialTerritories.Naadam);

                if (step.SinglePlayerDutyOptions?.Target != null)
                    yield return new SetTarget((uint)step.SinglePlayerDutyOptions.Target);

                yield return new WaitSinglePlayerDuty(cfcId);
                yield return new DisableAi();
                yield return new WaitForSinglePlayerDutyOutcome(cfcId, quest.Id, sequence.Sequence);
            }
        }

        private unsafe bool DutyActionsAvailable() => RaptureHotbarModule.Instance()->DutyActionsPresent;
    }

    internal sealed record StartSinglePlayerDuty(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"Wait(BossMod, entered instance {ContentFinderConditionId})";
    }

    internal sealed class StartSinglePlayerDutyExecutor(ICondition condition) : TaskExecutor<StartSinglePlayerDuty>
    {
        private DateTime _enteredAt = DateTime.MinValue;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            GameMain* gameMain = GameMain.Instance();
            if (gameMain->CurrentContentFinderConditionId != Task.ContentFinderConditionId)
                return ETaskResult.StillRunning;

            if (!condition[ConditionFlag.BoundByDuty])
                return ETaskResult.StillRunning;

            // we add a minimum wait time to try avoid issues with starting too early
            // could also be adding unnecessary wait time but needs more investigation ig
            if (_enteredAt == DateTime.MinValue)
                _enteredAt = DateTime.Now;

            return DateTime.Now - _enteredAt >= TimeSpan.FromSeconds(2)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record EnableAi(bool Passive = false) : ITask
    {
        public override string ToString() => $"BossMod.EnableAi({(Passive ? "Passive" : "AutoPull")})";
    }

    internal sealed class EnableAiExecutor
    (
        BossModIpc bossModIpc) : TaskExecutor<EnableAi>
    {
        protected override bool Start()
        {
            bossModIpc.SetPresetForSoloDuty(Task.Passive ? BossModIpc.EPreset.Overworld : BossModIpc.EPreset.QuestBattle);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record SetPreset(BossModIpc.EPreset Preset) : ITask
    {
        public override string ToString() => $"BossMod.SetPreset({Enum.GetName(Preset)})";
    }

    internal sealed class SetPresetExecutor
    (
        BossModIpc bossModIpc) : TaskExecutor<SetPreset>
    {
        protected override bool Start()
        {
            bossModIpc.SetPresetForSoloDuty(Task.Preset);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitSinglePlayerDuty(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"Wait(BossMod, left instance {ContentFinderConditionId})";
    }

    internal sealed class WaitSinglePlayerDutyExecutor
    (
        BossModIpc bossModIpc,
        MovementController movementController)
        : TaskExecutor<WaitSinglePlayerDuty>, IStoppableTaskExecutor, IDebugStateProvider
    {
        public string? GetDebugState()
        {
            if (!movementController.IsNavmeshReady)
                return $"Navmesh: {movementController.BuiltNavmeshPercent}%";

            return null;
        }

        public override unsafe ETaskResult Update()
        {
            return GameMain.Instance()->CurrentContentFinderConditionId != Task.ContentFinderConditionId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => bossModIpc.Cleanup();

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start() => true;
    }

    internal sealed record DisableAi : ITask
    {
        public override string ToString() => "BossMod.DisableAi";
    }

    internal sealed class DisableAiExecutor
    (
        BossModIpc bossModIpc) : TaskExecutor<DisableAi>
    {
        protected override bool Start()
        {
            bossModIpc.DisableSoloDutyPreset();
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }

    // TODO this should be handled in VBM
    internal sealed record SetTarget(uint DataId) : ITask
    {
        public override string ToString() => $"SetTarget({DataId})";
    }

    internal sealed class SetTargetExecutor
    (
        ITargetManager targetManager,
        IObjectTable objectTable) : TaskExecutor<SetTarget>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            if (GameFunctions.GetBaseID(targetManager.Target) == Task.DataId)
                return ETaskResult.TaskComplete;

            IGameObject? gameObject = objectTable.FirstOrDefault(x => GameFunctions.GetBaseID(x) == Task.DataId);
            if (gameObject == null)
                return ETaskResult.StillRunning;

            targetManager.Target = gameObject;
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    /// <summary>
    ///     Sits after <see cref="WaitSinglePlayerDuty"/> + <see cref="DisableAi"/>. Normally the controller's
    ///     sequence-change watcher in UpdateCurrentQuest clears this task and moves on; if no quest progress
    ///     happens within a grace period after leaving the instance, we assume the duty failed and ask the
    ///     controller to re-run the current step.
    /// </summary>
    internal sealed record WaitForSinglePlayerDutyOutcome(
        uint ContentFinderConditionId,
        ElementId QuestId,
        byte StartSequence) : ITask
    {
        public override string ToString() =>
            $"WaitForSinglePlayerDutyOutcome({ContentFinderConditionId}, seq={StartSequence})";
    }

    internal sealed class WaitForSinglePlayerDutyOutcomeExecutor(
        GameFunctions gameFunctions,
        ICondition condition,
        IChatGui chatGui,
        ILogger<WaitForSinglePlayerDutyOutcomeExecutor> logger)
        : TaskExecutor<WaitForSinglePlayerDutyOutcome>, IDebugStateProvider
    {
        private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(15);
        private DateTime _readyAt = DateTime.MinValue;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            // Reset the grace timer whenever the player is busy (cutscene, loading screen,
            // animation lock, etc.) — the quest sequence won't update during these.
            if (gameFunctions.IsOccupied() ||
                condition[ConditionFlag.WatchingCutscene] ||
                condition[ConditionFlag.WatchingCutscene78] ||
                condition[ConditionFlag.BetweenAreas] ||
                condition[ConditionFlag.BetweenAreas51])
            {
                _readyAt = DateTime.Now;
                return ETaskResult.StillRunning;
            }

            // If somehow we're back inside the same instance (shouldn't happen — WaitSinglePlayerDuty
            // already confirmed we exited), keep waiting.
            if (GameMain.Instance()->CurrentContentFinderConditionId == Task.ContentFinderConditionId)
            {
                _readyAt = DateTime.Now;
                return ETaskResult.StillRunning;
            }

            if (_readyAt == DateTime.MinValue)
                _readyAt = DateTime.Now;

            if (DateTime.Now - _readyAt < GracePeriod)
                return ETaskResult.StillRunning;

            // If the in-game quest sequence advanced (or the quest is gone), the duty succeeded —
            // QuestController.UpdateCurrentQuest will clear us; just keep waiting.
            QuestProgressInfo? progress = QuestFunctions.GetQuestProgressInfo(Task.QuestId);
            if (progress == null || progress.Sequence != Task.StartSequence)
                return ETaskResult.StillRunning;

            int failures = RecordRetry(Task.QuestId, Task.ContentFinderConditionId, Task.StartSequence);
            if (failures >= MaxRetries)
            {
                logger.LogError(
                    "SinglePlayerDuty {Cfc} failed {Failures} times — stopping",
                    Task.ContentFinderConditionId, failures);
                chatGui.PrintError(
                    $"You failed this instance {MaxRetries} times, Questionable is stopping. Please check it manually and report any issues to the instance.",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                ClearRetryCount();
                return ETaskResult.End;
            }

            logger.LogWarning(
                "SinglePlayerDuty {Cfc} appears to have failed (attempt {Failures}/{Max}, no quest progress {Seconds}s after leaving instance) — retrying step",
                Task.ContentFinderConditionId, failures, MaxRetries, GracePeriod.TotalSeconds);
            return ETaskResult.RetryStep;
        }

        public string? GetDebugState()
        {
            if (_readyAt == DateTime.MinValue)
                return "Waiting for player to be idle";
            TimeSpan remaining = GracePeriod - (DateTime.Now - _readyAt);
            return remaining > TimeSpan.Zero
                ? $"Outcome check in {remaining.TotalSeconds:F0}s"
                : "Outcome check pending";
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    // TODO valentiones hack
    internal sealed record Commence(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"Commence({ContentFinderConditionId})";
    }

    internal sealed class CommenceExecutor(ICondition condition) : TaskExecutor<Commence>
    {
        private DateTime _enteredAt = DateTime.MinValue;
        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            if (GenericHelpers.TryGetAddonMaster(out AddonMaster.ContentsFinderConfirm m) && m.IsAddonReady)
            {
                if (EzThrottler.Throttle("Confirm", 2000))
                    m.Commence();
            }

            GameMain* gameMain = GameMain.Instance();
            if (gameMain->CurrentContentFinderConditionId != Task.ContentFinderConditionId)
                return ETaskResult.StillRunning;

            if (!condition[ConditionFlag.BoundByDuty])
                return ETaskResult.StillRunning;
            if (_enteredAt == DateTime.MinValue)
                _enteredAt = DateTime.Now;

            return DateTime.Now - _enteredAt >= TimeSpan.FromSeconds(2)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record LeaveParty : ITask
    {
        public override string ToString() => "LeaveParty()";
    }

    internal sealed class LeavePartyExecutor(ICommandManager commandManager) : TaskExecutor<LeaveParty>
    {
        protected override bool Start() => LeavePartyAction();

        public override ETaskResult Update()
        {
            return LeavePartyAction() ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        public bool LeavePartyAction()
        {
            commandManager.ProcessCommand("/leave"); // TODO find a cleaner way to do this, and also a thing to check if it's done
            return true;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    public static unsafe bool ShouldLeaveParty
    {
        get
        {
            GroupManager* groupManager = GroupManager.Instance();
            var memberCount = groupManager->MainGroup.MemberCount;
            Svc.Log.Debug($"ShouldLeaveParty: {memberCount > 1} {memberCount}");
            return memberCount > 1;
        }
    }
}
