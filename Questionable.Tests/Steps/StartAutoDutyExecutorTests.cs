// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Data;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class StartAutoDutyExecutorTests
{
    [Fact]
    public void Combat_WaitsWithoutDispatch()
    {
        using var operations = new ExternalOperationFixture();
        operations.Condition[ConditionFlag.InCombat].Returns(true);
        var provider = operations.Provider<AutoDutyProvider>();
        var executor = new Duty.StartAutoDutyExecutor(null!, provider, null!, new(), null!, operations.Condition);

        Assert.True(executor.Start(new Duty.StartAutoDutyTask(57, Duty.Mode.Support)));
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        Assert.False(provider.HasOperation);
        Assert.True(executor.WasInterrupted());
        Assert.True(executor.ShouldInterruptOnDamage());
        Assert.False(provider.HandlesPlayerDeath(executor));
    }

    [Fact]
    public void CombatEnds_ResumesDutyValidation()
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(true);
        var territories = (TerritoryData)RuntimeHelpers.GetUninitializedObject(typeof(TerritoryData));
        typeof(TerritoryData).GetField("_cfcs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(territories, ImmutableDictionary<uint, TerritoryData.CfcData>.Empty);
        var executor = new Duty.StartAutoDutyExecutor(null!, null!, territories, new(), null!, condition);
        Assert.True(executor.Start(new Duty.StartAutoDutyTask(57, Duty.Mode.Support)));
        Assert.Equal(ETaskResult.StillRunning, executor.Update());

        condition[ConditionFlag.InCombat].Returns(false);
        Assert.False(executor.WasInterrupted());
        var error = Assert.Throws<TaskException>(() => executor.Update());
        Assert.Equal("Failed to get territory ID for content finder condition", error.Message);
    }

    [Fact]
    public void WaitingDeath_ConfirmsReturn()
    {
        using var operations = new ExternalOperationFixture();
        operations.Condition[ConditionFlag.InCombat].Returns(true);
        var provider = operations.Provider<AutoDutyProvider>();
        var executor = new Duty.StartAutoDutyExecutor(null!, provider, null!, new(), null!, operations.Condition);
        var queue = new TaskQueue(NullLogger.Instance);
        Assert.True(queue.StartTask(executor, new Duty.StartAutoDutyTask(57, Duty.Mode.Support)));
        var host = Substitute.For<IQuestRecoveryHost>();
        host.IsLoggedIn.Returns(true);
        host.IsUnconscious.Returns(true);
        host.HasQueuedTasks.Returns(_ => !queue.AllTasksComplete);
        host.ProviderHandlesDeath.Returns(_ => provider.HandlesPlayerDeath(queue.CurrentTaskExecutor));
        host.HasExternalOperation.Returns(_ => provider.HasOperation);
        host.ClearTasks().Returns(_ => queue.Reset());
        var recovery = new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance);

        Assert.True(recovery.Update(host));
        Assert.True(recovery.IsRecovering);
        Assert.True(queue.AllTasksComplete);
        host.Received(1).TryConfirmReturnPrompt();
        host.DidNotReceive().Stop(Arg.Any<string>());
        Assert.False(provider.HasOperation);
    }
}
