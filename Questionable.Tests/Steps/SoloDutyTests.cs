using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class SoloDutyTests
{
    private readonly BossModIpcTests.BossModTestContext _context = new();

    [Theory]
    [InlineData(123, "Questionable - Quest Battles")]
    [InlineData(454, "Questionable - Quest Battles")]
    [InlineData(461, "Questionable - Quest Battles")]
    [InlineData(1016, "Questionable - Quest Battles")]
    [InlineData(867, "Questionable - Quest Battles")]
    [InlineData(1009, "Questionable - Quest Battles")]
    [InlineData(322, "Questionable - Quest Battles")]
    [InlineData(323, "Questionable - Quest Battles")]
    [InlineData(237, "Questionable - Quest Battles")]
    [InlineData(246, "Questionable - Quest Battles")]
    [InlineData(1045, "Questionable - Normal Movement")]
    [InlineData(250, "Questionable - Quest Battles")]
    [InlineData(1001, "Questionable - Quest Battles")]
    [InlineData(994, "Questionable - Quest Battles")]
    [InlineData(1101, "Questionable - Quest Battles")]
    public void OrdinaryAndMigratedDuties_ActivateDirectlyWithoutCapabilityEndpoint(uint cfcId, string expectedPreset)
    {
        var originalSettings = _context.Settings.ToDictionary(x => x.Key, x => x.Value);
        var executor = new SinglePlayerDuty.EnableAiExecutor(_context.Ipc);
        Assert.True(executor.Start(new SinglePlayerDuty.EnableAi(cfcId)));
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
        Assert.Equal(expectedPreset, _context.ActivePreset);
        _context.SetPreset.Received(1).InvokeFunc(expectedPreset);
        Assert.True(_context.Ipc.HasOwnedSoloDuty);
        Assert.True(_context.Settings[("ZoneModuleConfig", "EnableQuestBattles")]);
        Assert.False(_context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")]);
        Assert.False(_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.True(_context.Settings[("ActionTweaksConfig", "PreventMovingWhileCasting")]);
        Assert.False(_context.Settings[("AIConfig", "Enabled")]);

        _context.Ipc.PollSoloDuty();
        Assert.False(_context.Ipc.HasPendingCleanup);
        var disable = new SinglePlayerDuty.DisableAiExecutor(_context.Ipc);
        Assert.True(disable.Start(new SinglePlayerDuty.DisableAi()));
        Assert.Equal(ETaskResult.TaskComplete, disable.Update());
        Assert.False(_context.Ipc.HasOwnedSoloDuty);
        Assert.False(_context.Ipc.HasPendingCleanup);
        Assert.Equal("User preset", _context.ActivePreset);
        Assert.All(originalSettings, setting => Assert.Equal(setting.Value, _context.Settings[setting.Key]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingProvider_StopsBeforePresetMutation(bool throws)
    {
        if (throws)
            _context.GetActivePreset.Configure().InvokeFunc().Returns(_ => throw new InvalidOperationException("lost provider"));
        else
            _context.GetActivePreset.HasFunction.Returns(false);
        Assert.Throws<TaskException>(() => _context.Ipc.StartSoloDuty(454));
        Assert.Empty(_context.Writes);
        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Theory]
    [InlineData(454)]
    [InlineData(461)]
    [InlineData(1016)]
    [InlineData(867)]
    [InlineData(1009)]
    [InlineData(322)]
    [InlineData(323)]
    [InlineData(237)]
    [InlineData(246)]
    [InlineData(1045)]
    [InlineData(250)]
    [InlineData(1001)]
    [InlineData(994)]
    [InlineData(1101)]
    public void EveryMigratedDuty_UsesOnlyTheGenericHandoff(uint cfcId)
    {
        var questId = new Questionable.Model.Questing.QuestId(1);
        var tasks = SinglePlayerDuty.Factory.CreateDutyTasks(cfcId, questId, 2).ToArray();
        Assert.Equal(6, tasks.Length);
        Assert.IsType<Questionable.Controller.Steps.Common.MountStep.UnmountTask>(tasks[0]);
        Assert.Equal(cfcId, Assert.IsType<SinglePlayerDuty.StartSinglePlayerDuty>(tasks[1]).ContentFinderConditionId);
        Assert.Equal(cfcId, Assert.IsType<SinglePlayerDuty.EnableAi>(tasks[2]).ContentFinderConditionId);
        Assert.Equal(cfcId, Assert.IsType<SinglePlayerDuty.WaitSinglePlayerDuty>(tasks[3]).ContentFinderConditionId);
        Assert.IsType<SinglePlayerDuty.DisableAi>(tasks[4]);
        Assert.Equal(new SinglePlayerDuty.WaitForSinglePlayerDutyOutcome(cfcId, questId, 2), tasks[5]);
    }

    [Theory]
    [InlineData(1045, 1045, true, true)]
    [InlineData(1045, 867, true, false)]
    [InlineData(1045, 1045, false, false)]
    [InlineData(0, 0, true, false)]
    public void EntryConfirmation_OnlyAcceptsTheRequestedDuty(uint requested, uint popped, bool regularDuty, bool expected)
    {
        Assert.Equal(expected, SinglePlayerDuty.StartSinglePlayerDutyExecutor.CanConfirmEntry(requested, popped, regularDuty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryDeath_RetainsOwnedPresetForNativeRetryAndRestoresActualPolicy(bool initiallyEnabled)
    {
        _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")] = initiallyEnabled;
        var executor = new SinglePlayerDuty.EnableAiExecutor(_context.Ipc);
        Assert.True(executor.Start(new SinglePlayerDuty.EnableAi(454)));
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
        string? ownedPreset = _context.ActivePreset;

        // Model the provider's configurable ordinary-death and combat-end transitions.
        // ScriptedDeath is deliberately not involved in this generic lifetime policy.
        if (_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")])
            _context.ActivePreset = "Force disabled";
        _context.Ipc.PollSoloDuty();
        if (_context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")])
            _context.ActivePreset = null;
        _context.Ipc.PollSoloDuty();

        Assert.Equal(ownedPreset, _context.ActivePreset);
        Assert.True(_context.Ipc.HasOwnedSoloDuty);
        Assert.False(_context.Ipc.HasPendingCleanup);
        Assert.Null(_context.Ipc.CleanupReason);

        var disable = new SinglePlayerDuty.DisableAiExecutor(_context.Ipc);
        Assert.True(disable.Start(new SinglePlayerDuty.DisableAi()));
        Assert.Equal(ETaskResult.TaskComplete, disable.Update());
        Assert.Equal(initiallyEnabled, _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.Equal(initiallyEnabled ? 2 : 0, _context.Writes.Count(x => x.Field == "ClearPresetOnDeath"));
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Fact]
    public void EnableCompletion_KeepsOwnershipAcrossIntermediateTasksUntilControllerCleanup()
    {
        var executor = new SinglePlayerDuty.EnableAiExecutor(_context.Ipc);
        var queue = new TaskQueue(NullLogger.Instance);
        Assert.True(queue.StartTask(executor, new SinglePlayerDuty.EnableAi(454)));
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
        queue.CurrentTaskExecutor = null; // MiniTaskController's normal completion path

        var wait = new WaitAtStart.WaitDelayExecutor();
        Assert.True(queue.StartTask(wait, new WaitAtStart.WaitDelay(TimeSpan.FromSeconds(2))));
        _context.Ipc.PollSoloDuty();
        Assert.True(_context.Ipc.HasOwnedSoloDuty);
        Assert.False(_context.Ipc.HasPendingCleanup);
        Assert.Null(_context.Ipc.CleanupReason);

        Assert.True(queue.Reset()); // An intermediate local task does not own the BossMod cleanup.
        Assert.True(_context.Ipc.HasOwnedSoloDuty);
        _context.Ipc.DisableSoloDutyPreset(); // QuestController's shared reset hook
        Assert.False(_context.Ipc.HasOwnedSoloDuty);
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Theory]
    [InlineData(454, "Questionable - Quest Battles")]
    [InlineData(1045, "Questionable - Normal Movement")]
    public void ResetDuringActivationTask_ReleasesDutySelectedPreset(uint cfcId, string expectedPreset)
    {
        var originalSettings = _context.Settings.ToDictionary(x => x.Key, x => x.Value);
        var queue = new TaskQueue(NullLogger.Instance);
        Assert.True(queue.StartTask(new SinglePlayerDuty.EnableAiExecutor(_context.Ipc), new SinglePlayerDuty.EnableAi(cfcId)));
        Assert.Equal(expectedPreset, _context.ActivePreset);
        Assert.True(queue.Reset());
        Assert.False(_context.Ipc.HasOwnedSoloDuty);
        Assert.False(_context.Ipc.HasPendingCleanup);
        Assert.Equal("User preset", _context.ActivePreset);
        Assert.All(originalSettings, setting => Assert.Equal(setting.Value, _context.Settings[setting.Key]));
    }
    [Fact]
    public void FailedActivationTask_CleansPartialAcquisitionThroughTaskQueue()
    {
        var queue = new TaskQueue(NullLogger.Instance);
        _context.AfterSettingWrite = (_, field, value) =>
        {
            if (field == "EnableQuestBattles" && value)
                throw new InvalidOperationException("lost response after applying setting");
        };
        Assert.Throws<TaskException>(() => queue.StartTask(new SinglePlayerDuty.EnableAiExecutor(_context.Ipc),
            new SinglePlayerDuty.EnableAi(454)));
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.False(_context.Ipc.HasPendingCleanup);
        Assert.False(_context.Settings[("ZoneModuleConfig", "EnableQuestBattles")]);
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Fact]
    public void FailedReset_DoesNotLoseCleanupWhenExecutorIsDetached()
    {
        _context.ActivePreset = null;
        var queue = new TaskQueue(NullLogger.Instance);
        queue.StartTask(new SinglePlayerDuty.EnableAiExecutor(_context.Ipc), new SinglePlayerDuty.EnableAi(454));
        _context.FailClearPreset = true;
        Assert.False(queue.Reset());
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.True(_context.Ipc.HasPendingCleanup);
        _context.FailClearPreset = false;
        _context.Ipc.PollSoloDuty();
        Assert.False(_context.Ipc.HasPendingCleanup);
    }

    [Fact]
    public void ManualTakeoverBetweenTasks_StopsContinuationWithoutClearingManualPreset()
    {
        var executor = new SinglePlayerDuty.EnableAiExecutor(_context.Ipc);
        executor.Start(new SinglePlayerDuty.EnableAi(454));
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
        _context.ActivePreset = "Manual preset";
        Assert.Throws<TaskException>(_context.Ipc.PollSoloDuty);
        Assert.Throws<TaskException>(() => new SinglePlayerDuty.EnableAiExecutor(_context.Ipc)
            .Start(new SinglePlayerDuty.EnableAi(454)));
        Assert.Equal("Manual preset", _context.ActivePreset);
    }

    [Fact]
    public void DisableTask_DoesNotCompleteWhenRestorationIsRejected()
    {
        _context.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.RejectActivation = true;
        var executor = new SinglePlayerDuty.DisableAiExecutor(_context.Ipc);
        Assert.Throws<TaskException>(() => executor.Start(new SinglePlayerDuty.DisableAi()));
        Assert.True(_context.Ipc.HasPendingCleanup);
        _context.RejectActivation = false;
        executor.StopNow();
        Assert.False(_context.Ipc.HasPendingCleanup);
    }
}
