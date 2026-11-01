using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Data;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AutoDutyIpcTests
{
    [Fact]
    public void AdvertisedApiWithFailedStatus_IsAvailableWithoutReadingBusyState()
    {
        using var f = new AutoDutyFixture();
        f.StoppedGate.InvokeFunc().Returns(_ => throw new InvalidOperationException("Status read failed"));

        Assert.True(f.Ipc.IsAvailable());
        f.StoppedGate.DidNotReceive().InvokeFunc();
        Assert.Null(f.Ipc.GetStopped());
        Assert.False(f.Ipc.IsReady());
        Assert.False(f.State.HasOperation);
    }

    [Fact]
    public void NoAdvertisedLifecycleApi_IsAbsent()
    {
        using var f = new AutoDutyFixture { Available = false };

        Assert.False(f.Ipc.IsAvailable());
        Assert.Null(f.Ipc.GetStopped());
        f.StoppedGate.DidNotReceive().InvokeFunc();
    }

    [Theory]
    [InlineData("Run")]
    [InlineData("Stop")]
    [InlineData("IsStopped")]
    public void AnyAdvertisedLifecycleEndpoint_IsEnoughToProtectWork(string endpoint)
    {
        using var f = new AutoDutyFixture { Available = false };
        f.Run.HasAction.Returns(endpoint == "Run");
        f.Stop.HasAction.Returns(endpoint == "Stop");
        f.StoppedGate.HasFunction.Returns(endpoint == "IsStopped");

        Assert.True(f.Ipc.IsAvailable());
        Assert.Null(f.Ipc.GetStopped());
        f.StoppedGate.DidNotReceive().InvokeFunc();
    }

    [Fact]
    public void EndpointLookupFailure_ConservativelyProtectsPossibleManualWork()
    {
        using var f = new AutoDutyFixture();
        f.Run.HasAction.Returns(_ => throw new InvalidOperationException("Endpoint lookup failed"));

        Assert.True(f.Ipc.IsAvailable());
        Assert.Null(f.Ipc.GetStopped());
        f.StoppedGate.DidNotReceive().InvokeFunc();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Readiness_IsIndependentOfRunning(bool stopped)
    {
        using var f = new AutoDutyFixture { Stopped = stopped };
        Assert.True(f.Ipc.IsReady());
        f.Available = false;
        Assert.False(f.Ipc.IsReady());
        Assert.False(f.Ipc.IsStopped());
        Assert.Null(f.Ipc.GetStopped());
    }

    [Fact]
    public void ManualBusy_IsNeverReconfiguredStartedOrStopped()
    {
        using var f = new AutoDutyFixture { Stopped = false };
        Assert.Throws<TaskException>(f.Start);
        f.Ipc.CancelOwned();
        Assert.False(f.State.HasOperation);
        Assert.Empty(f.Writes);
        f.Run.DidNotReceiveWithAnyArgs().InvokeAction(default, default, default);
        f.Stop.DidNotReceive().InvokeAction();
    }

    [Theory]
    [InlineData((int)AutoDutyIpc.DutyMode.Support)]
    [InlineData((int)AutoDutyIpc.DutyMode.Regular)]
    [InlineData((int)AutoDutyIpc.DutyMode.Variant)]
    [InlineData((int)AutoDutyIpc.DutyMode.Trust)]
    public void BlacklistAndConfiguration_ApplyToEveryMode(int mode)
    {
        using var f = new AutoDutyFixture();
        f.Configuration.Duties.BlacklistedDutyCfcIds.Add(AutoDutyFixture.CfcId);
        f.Configuration.Duties.WhitelistedDutyCfcIds.Add(AutoDutyFixture.CfcId);
        Assert.False(f.Ipc.IsConfiguredToRunContent(new DutyOptions { ContentFinderConditionId = AutoDutyFixture.CfcId, Enabled = true }));
        Assert.Throws<TaskException>(() => f.Ipc.StartInstance(AutoDutyFixture.CfcId, (AutoDutyIpc.DutyMode)mode));
        f.Configuration.Duties.BlacklistedDutyCfcIds.Clear();
        f.Configuration.Duties.RunInstancedContentWithAutoDuty = false;
        Assert.Throws<TaskException>(() => f.Ipc.StartInstance(AutoDutyFixture.CfcId, (AutoDutyIpc.DutyMode)mode));
        Assert.Empty(f.Writes);
        f.Run.DidNotReceiveWithAnyArgs().InvokeAction(default, default, default);
    }

    [Fact]
    public void MissingSettingsGetter_RejectsBeforeAnyMutation()
    {
        using var f = new AutoDutyFixture();
        f.GetConfig.HasFunction.Returns(false);
        Assert.Throws<TaskException>(f.Start);
        Assert.False(f.State.HasOperation);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public void UnsupportedNestedSetting_RejectsBeforeAnyMutation()
    {
        using var f = new AutoDutyFixture();
        f.Settings[AutoDutyFixture.ModeKey] = "";
        Assert.Throws<TaskException>(f.Start);
        Assert.False(f.State.HasOperation);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public void VoidRunWithoutActivity_TimesOutAndRequiresConfirmation()
    {
        using var f = new AutoDutyFixture { RunStarts = false };
        f.Start();
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.ObservedRunning);
        Assert.False(f.State.IsTerminal);
        f.PollFor(18);
        Assert.True(f.State.RequiresReconciliation);
        Assert.True(f.State.CanConfirmIdle);
        Assert.Throws<TaskException>(f.Start);
        Assert.True(f.Ipc.TryConfirmIdle());
        f.Stop.DidNotReceive().InvokeAction();
        f.Run.Received(1).InvokeAction(AutoDutyFixture.TerritoryId, 1, true);
        f.AssertOriginalSettings();
    }

    [Fact]
    public void RunThrowsAfterPossibleAcceptance_RetainsWorkWithoutGlobalStopOrRetry()
    {
        using var f = new AutoDutyFixture { ThrowRun = true };
        Assert.Throws<TaskException>(f.Start);
        Assert.True(f.State.RequiresReconciliation);
        Assert.True(f.State.HasOperation);
        f.Ipc.CancelOwned();
        Assert.Throws<TaskException>(f.Start);
        Assert.False(f.Ipc.TryConfirmIdle());
        f.Stop.DidNotReceive().InvokeAction();
        f.Run.Received(1).InvokeAction(AutoDutyFixture.TerritoryId, 1, true);
        f.Stopped = true;
        f.PollFor(3);
        Assert.True(f.Ipc.TryConfirmIdle());
        f.AssertOriginalSettings();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RealQueueOrLoading_DoesNotUseEntryDeadline(bool queue)
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Game = f.Game with { InQueue = queue, Loading = !queue };
        f.Advance(TimeSpan.FromHours(2));
        Assert.False(f.State.RequiresReconciliation);
        Assert.True(f.State.HasOperation);
        f.Game = f.Game with { InQueue = false, Loading = false };
        f.PollFor(2);
        Assert.False(f.State.RequiresReconciliation);
    }

    [Fact]
    public void RunningWithoutEntryOrQueue_HasBoundedFailure()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Advance(TimeSpan.FromMinutes(3));
        Assert.True(f.State.RequiresReconciliation);
        Assert.True(f.State.HasOperation);
        Assert.False(f.Ipc.TryConfirmIdle());
    }

    [Fact]
    public void StopReturn_IsNotAcknowledgement_AndRepeatedStopDoesNotTouchOtherWork()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Ipc.CancelOwned();
        Assert.True(f.State.HasOperation);
        Assert.True(f.State.IsAbandoned);
        f.Ipc.CancelOwned();
        f.Stop.Received(1).InvokeAction();
        Assert.False(f.Ipc.TryConfirmIdle());
        f.Stopped = true;
        f.Game = f.Game with { InQueue = true };
        f.PollFor(3);
        Assert.True(f.State.HasOperation);
        f.Game = f.Game with { InQueue = false };
        f.PollFor(3);
        Assert.False(f.State.HasOperation);
        f.AssertOriginalSettings();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopFailureOrTimeout_RemainsGuarded(bool throws)
    {
        using var f = new AutoDutyFixture { ThrowStop = throws };
        f.Start();
        f.Ipc.CancelOwned();
        f.PollFor(16);
        Assert.True(f.State.HasOperation);
        Assert.True(f.State.RequiresReconciliation);
        Assert.False(f.Ipc.TryConfirmIdle());
        f.Ipc.CancelOwned();
        f.Stop.Received(1).InvokeAction();
    }

    [Fact]
    public void ProviderLoss_NeverMeansStoppedOrCancelsReplacementProvider()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Available = false;
        f.Ipc.PollOperation();
        f.Ipc.CancelOwned();
        Assert.True(f.State.HasOperation);
        Assert.True(f.State.ProviderLost);
        Assert.False(f.Ipc.IsStopped());
        f.Available = true;
        f.Stopped = false; // a manual run in the reloaded provider
        f.Ipc.CancelOwned();
        f.Stop.DidNotReceive().InvokeAction();
        int writes = f.Writes.Count;
        f.Stopped = true;
        f.PollFor(3);
        Assert.False(f.State.CanConfirmIdle); // outstanding old settings cannot cross sessions
        Assert.Contains("Trust", f.State.Message, StringComparison.Ordinal);
        Assert.Equal(writes, f.Writes.Count);
        f.Settings[AutoDutyFixture.ModeKey] = "Trust";
        f.Settings[AutoDutyFixture.UnsyncedKey] = "True";
        f.PollFor(3);
        Assert.True(f.Ipc.TryConfirmIdle());
        Assert.Equal(writes, f.Writes.Count);
    }

    [Fact]
    public void ObservedStopThenManualRestart_DoesNotCancelNewWork()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Stopped = true;
        f.Ipc.PollOperation();
        f.Stopped = false;
        f.Ipc.CancelOwned();
        Assert.True(f.State.RequiresReconciliation);
        f.Stop.DidNotReceive().InvokeAction();
    }

    [Fact]
    public void NativeCompletionAndExitAreRequired_AndSettingsRestoreBeforeRelease()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Enter();
        f.RaiseCompleted();
        Assert.True(f.State.DutyCompleted);
        f.Stopped = true;
        f.PollFor(3);
        Assert.False(f.State.IsTerminal); // still inside the duty
        f.Exit();
        f.PollFor(3);
        Assert.Equal(ExternalOperationStatus.Succeeded, f.State.Status);
        f.AssertOriginalSettings();
        Assert.True(f.State.TryReleaseTerminal(f.State.OperationId));
    }

    [Fact]
    public void ProviderStoppedWithoutNativeCompletion_IsFailure()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Enter();
        f.Exit();
        f.Stopped = true;
        f.PollFor(3);
        Assert.Equal(ExternalOperationStatus.Failed, f.State.Status);
        f.AssertOriginalSettings();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalStop_WithSlowNativeExit_DoesNotRequireReconciliation(bool completed)
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Enter();
        if (completed)
            f.RaiseCompleted();
        f.Stopped = true;
        f.PollFor(30); // Native duty/cutscene remains active after the provider stops.
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.IsTerminal);
        Assert.False(f.State.RequiresReconciliation);
        Assert.Equal("Support", f.Settings[AutoDutyFixture.ModeKey]);

        f.Game = f.Game with { InDuty = false, Loading = true };
        f.PollFor(30); // A slow loading screen is not a failed Stop request either.
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.RequiresReconciliation);
        f.Exit();
        f.PollFor(3);

        Assert.Equal(completed ? ExternalOperationStatus.Succeeded : ExternalOperationStatus.Failed, f.State.Status);
        Assert.False(f.State.RequiresReconciliation);
        f.AssertOriginalSettings();
        f.Stop.DidNotReceive().InvokeAction();
        Assert.True(f.State.TryReleaseTerminal(f.State.OperationId));
    }

    [Fact]
    public void LaterUserSettingChange_IsPreserved()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Settings[AutoDutyFixture.ModeKey] = "Variant";
        f.Ipc.CancelOwned();
        f.Stopped = true;
        f.PollFor(3);
        Assert.False(f.State.HasOperation);
        Assert.Equal("Variant", f.Settings[AutoDutyFixture.ModeKey]);
        Assert.Equal("True", f.Settings[AutoDutyFixture.UnsyncedKey]);
        Assert.DoesNotContain(f.Writes, x => x == (AutoDutyFixture.ModeKey, "Trust"));
    }

    [Fact]
    public void FailedRestoration_RetainsObligationAndRequiresAcknowledgementAfterRetry()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.FailRestore = true;
        f.Ipc.CancelOwned();
        f.Stopped = true;
        f.PollFor(4);
        Assert.True(f.State.HasOperation);
        Assert.True(f.State.RequiresReconciliation);
        Assert.False(f.State.CanConfirmIdle);
        f.FailRestore = false;
        f.PollFor(3);
        f.AssertOriginalSettings();
        Assert.True(f.Ipc.TryConfirmIdle());
    }

    [Fact]
    public void SetterThrowsAfterChangingValue_RestoresPartialMutationWithoutRunning()
    {
        using var f = new AutoDutyFixture { ThrowAfterFirstWrite = true };
        Assert.Throws<TaskException>(f.Start);
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.RunSubmitted);
        f.PollFor(3);
        f.AssertOriginalSettings();
        Assert.True(f.Ipc.TryConfirmIdle());
        f.Run.DidNotReceiveWithAnyArgs().InvokeAction(default, default, default);
        f.Stop.DidNotReceive().InvokeAction();
    }

    [Fact]
    public void ChildMovementOrUnknownNativeState_PreventsRelease()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Ipc.CancelOwned();
        f.Stopped = true;
        f.PathRunning.InvokeFunc().Returns(true);
        f.PollFor(3);
        Assert.True(f.State.HasOperation);
        f.PathRunning.InvokeFunc().Returns(false);
        f.Game = f.Game with { Available = false };
        f.PollFor(3);
        Assert.True(f.State.HasOperation);
        f.Exit();
        f.PollFor(3);
        Assert.False(f.State.HasOperation);
    }

    [Fact]
    public void DisposingAdapter_UnsubscribesNativeCompletion()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Enter();
        f.Ipc.Dispose();
        f.RaiseCompleted();
        Assert.False(f.State.DutyCompleted);
    }
}
