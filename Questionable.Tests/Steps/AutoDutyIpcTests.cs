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
        Assert.Empty(f.Calls);
        f.RunForPlugin.DidNotReceiveWithAnyArgs().InvokeAction(default!, default, default, default);
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
        f.RunForPlugin.DidNotReceiveWithAnyArgs().InvokeAction(default!, default, default, default);
    }

    [Fact]
    public void MissingSettingsGetter_RejectsBeforeAnyMutation()
    {
        using var f = new AutoDutyFixture();
        f.GetConfig.HasFunction.Returns(false);
        Assert.Throws<TaskException>(f.Start);
        Assert.False(f.State.HasOperation);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void AutoDutyWithoutPluginProfiles_AsksForAnUpdateBeforeAnyChange()
    {
        using var f = new AutoDutyFixture();
        f.PluginConfig.HasAction.Returns(false);

        var e = Assert.Throws<TaskException>(f.Start);

        Assert.Contains("Update AutoDuty", e.Message, StringComparison.Ordinal);
        Assert.False(f.Ipc.IsReady());
        Assert.True(f.Ipc.IsAvailable());
        Assert.False(f.State.HasOperation);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void ProfileIsSwitchedBeforeAnySettingIsWritten()
    {
        using var f = new AutoDutyFixture();
        f.Start();

        Assert.Equal("profile on", f.Calls[0]);
        Assert.Equal([$"set {AutoDutyFixture.UnsyncedKey}", $"set {AutoDutyFixture.ModeKey}"], f.Calls.Skip(1).Take(2));
        Assert.All(f.Calls.Skip(1).SkipLast(1), call => Assert.StartsWith("set ", call, StringComparison.Ordinal));
        Assert.Equal("run", f.Calls[^1]);
        Assert.Equal("Support", f.ProfileSettings![AutoDutyFixture.ModeKey]);
        Assert.Equal("False", f.ProfileSettings[AutoDutyFixture.UnsyncedKey]);
        Assert.Equal("Trust", f.Settings[AutoDutyFixture.ModeKey]);
        Assert.Equal("True", f.Settings[AutoDutyFixture.UnsyncedKey]);
    }

    [Fact]
    public void RiskyCopiedUserSettings_AreForcedForQuestRuns_AndUserProfileIsUntouched()
    {
        using var f = new AutoDutyFixture();
        var risky = new Dictionary<string, string>
        {
            ["Loop.Termination.TerminationMethodEnum"] = "Kill_PC",
            ["DutyConfig.AutoExitDuty"] = "False",
            [$"{AutoDutyFixture.Pre}[RetireLoopActionConfig].Enabled"] = "True",
            [$"{AutoDutyFixture.Pre}[ExecuteCommandsLoopActionConfig].Enabled"] = "True",
            [$"{AutoDutyFixture.Between}[AutoRetainerMultiModeLoopActionConfig].Enabled"] = "True",
            [$"{AutoDutyFixture.Between}[DiscardItemsLoopActionConfig].Enabled"] = "True",
            [$"{AutoDutyFixture.Between}[DesynthLoopActionConfig].Enabled"] = "True",
            ["DutyConfig.LootBossTreasureOnly"] = "True",
        };
        foreach (var (key, value) in risky)
            f.Settings[key] = value;
        f.Settings[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].Enabled"] = "False";
        f.Settings[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].AutoExtractAll"] = "True";

        f.Start();

        var profile = f.ProfileSettings!;
        Assert.Equal("False", profile["Loop.Termination.Enabled"]);
        Assert.Equal("Do_Nothing", profile["Loop.Termination.TerminationMethodEnum"]);
        Assert.Equal("True", profile["DutyConfig.AutoExitDuty"]);
        Assert.Equal("True", profile["DutyConfig.OnlyExitWhenDutyDone"]);
        Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[RetireLoopActionConfig].Enabled"]);
        Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[ExecuteCommandsLoopActionConfig].Enabled"]);
        Assert.Equal("False", profile["Loop.Between.Enabled"]);
        Assert.Equal("False", profile["DutyConfig.LootBossTreasureOnly"]);
        Assert.Equal("All", profile["DutyConfig.LootMethodEnum"]);
        Assert.Equal("False", profile["DutyConfig.Stuck.StuckReturn"]);
        Assert.Equal("False", profile["DutyConfig.AutoManageVnavAlignCamera"]);
        Assert.Equal("True", profile["DutyConfig.PathDrawEnabled"]);
        Assert.Equal("True", profile["Loop.Pre.Enabled"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[RepairLoopActionConfig].Enabled"]);
        Assert.Equal("50", profile[$"{AutoDutyFixture.Pre}[RepairLoopActionConfig].AutoRepairPct"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[AutoEquipLoopActionConfig].Enabled"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].Enabled"]);
        Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].AutoExtractAll"]);
        Assert.All(AutoDutyIpc.ProfileSettings.Where(s => profile.ContainsKey(s.Key)),
            s => Assert.Equal(s.Value, profile[s.Key], ignoreCase: true));
        Assert.All(risky, setting => Assert.Equal(setting.Value, f.Settings[setting.Key]));
    }

    [Fact]
    public void PersonalActionsAddedToUserLists_AreSwitchedOff_AndRepairAndGearArePinned()
    {
        using var f = new AutoDutyFixture();
        string[] personalPre =
        [
            "CofferOpenLoopActionConfig", "ArmoireLoopActionConfig", "GlamourLoopActionConfig",
            "GCTurnInLoopActionConfig", "ShopLoopActionConfig",
        ];
        foreach (string type in personalPre)
            f.Settings[$"{AutoDutyFixture.Pre}[{type}].Enabled"] = "True";
        f.Settings[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].Enabled"] = "True";
        f.Settings[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].AutoExtractAll"] = "True";
        f.Settings[$"{AutoDutyFixture.Between}[RepairLoopActionConfig].Enabled"] = "True";
        f.Settings[$"{AutoDutyFixture.Pre}[AutoEquipLoopActionConfig].RecommendedGearSource"] = "Stylist";
        f.Settings[$"{AutoDutyFixture.Pre}[RepairLoopActionConfig].AutoRepairSelf"] = "True";

        f.Start();

        var profile = f.ProfileSettings!;
        Assert.All(personalPre, type => Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[{type}].Enabled"]));
        Assert.Equal("False", profile["Loop.Between.Enabled"]);
        Assert.Equal("Vanilla", profile[$"{AutoDutyFixture.Pre}[AutoEquipLoopActionConfig].RecommendedGearSource"]);
        Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[RepairLoopActionConfig].AutoRepairSelf"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[RepairLoopActionConfig].Enabled"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[AutoEquipLoopActionConfig].Enabled"]);
        Assert.Equal("True", profile[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].Enabled"]);
        Assert.Equal("False", profile[$"{AutoDutyFixture.Pre}[ExtractLoopActionConfig].AutoExtractAll"]);
        Assert.All(personalPre, type => Assert.Equal("True", f.Settings[$"{AutoDutyFixture.Pre}[{type}].Enabled"]));
        Assert.Equal("Stylist", f.Settings[$"{AutoDutyFixture.Pre}[AutoEquipLoopActionConfig].RecommendedGearSource"]);
    }

    [Fact]
    public void EveryOtherPreLoopActionType_IsForcedOff_AndAfterRunIsOffAsAWhole()
    {
        string[] wanted = ["RepairLoopActionConfig", "AutoEquipLoopActionConfig", "ExtractLoopActionConfig"];
        string[] allTypes =
        [
            "AutoEquip", "Repair", "AutoRetainer", "AutoRetainerMultiMode", "DiscardItems", "TripleTriadUse",
            "TripleTriadSell", "Armoire", "Glamour", "Extract", "GCTurnIn", "Desynth", "CofferOpen", "Shop",
            "Retire", "ConsumeItems", "ExecuteCommands", "Wait", "PlaySound",
        ];
        var forcedOff = AutoDutyIpc.ProfileSettings
            .Where(s => s.Kind == AutoDutyIpc.SettingKind.ForceOff)
            .Select(s => s.Key)
            .ToHashSet();

        foreach (string type in allTypes.Select(t => $"{t}LoopActionConfig"))
            Assert.Equal(!wanted.Contains(type), forcedOff.Contains($"{AutoDutyFixture.Pre}[{type}].Enabled"));
        Assert.All(forcedOff, key => Assert.StartsWith(AutoDutyFixture.Pre, key, StringComparison.Ordinal));
        Assert.All(AutoDutyIpc.ProfileSettings.Where(s => s.Kind == AutoDutyIpc.SettingKind.ForceOff),
            s => Assert.Equal("False", s.Value));
        Assert.Contains(AutoDutyIpc.ProfileSettings, s => s is { Key: "Loop.Between.Enabled", Value: "False", Kind: AutoDutyIpc.SettingKind.Required });
    }

    [Fact]
    public void RunUsesTheProfilesLoopSettings()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.RunForPlugin.Received(1).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
        f.RunForPlugin.DidNotReceive().InvokeAction(Arg.Any<string>(), Arg.Any<uint>(), Arg.Any<int>(), true);
    }

    [Fact]
    public void SettingsAlreadyInPlace_AreNotWrittenAgain()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        f.Enter();
        f.RaiseCompleted();
        f.Stopped = true;
        f.Exit();
        f.PollFor(3);
        Assert.True(f.State.TryReleaseTerminal(f.State.OperationId));
        f.Writes.Clear();

        f.Start();

        Assert.Empty(f.Writes);
        f.RunForPlugin.Received(2).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
    }

    [Fact]
    public void ReadBackInDifferentCase_CountsAsApplied()
    {
        using var f = new AutoDutyFixture();
        f.Settings["DutyConfig.AutoExitDuty"] = "true";
        f.Settings["Loop.Termination.TerminationMethodEnum"] = "do_nothing";

        f.Start();

        Assert.DoesNotContain(f.Writes, w => w.Key is "DutyConfig.AutoExitDuty" or "Loop.Termination.TerminationMethodEnum");
    }

    [Fact]
    public void MissingRequiredSetting_RefusesToRunAndAsksForUpdate()
    {
        using var f = new AutoDutyFixture();
        f.Settings.Remove("Loop.Termination.TerminationMethodEnum");

        var e = Assert.Throws<TaskException>(f.Start);

        Assert.Contains("Update AutoDuty", e.Message, StringComparison.Ordinal);
        Assert.Contains("Loop.Termination.TerminationMethodEnum", e.Message, StringComparison.Ordinal);
        Assert.False(f.State.RunSubmitted);
        f.RunForPlugin.DidNotReceiveWithAnyArgs().InvokeAction(default!, default, default, default);
        f.PollFor(3);
        f.AssertUserProfileRestored();
        Assert.True(f.Ipc.TryConfirmIdle());
    }

    [Theory]
    [InlineData("Loop.Pre.Actions.[RepairLoopActionConfig]")]
    [InlineData("Loop.Pre.Actions.[AutoEquipLoopActionConfig]")]
    [InlineData("Loop.Pre.Actions.[ExtractLoopActionConfig]")]
    public void WantedActionDeletedByUser_RunsWithoutIt(string action)
    {
        using var f = new AutoDutyFixture();
        foreach (string key in f.Settings.Keys.Where(k => k.StartsWith(action, StringComparison.Ordinal)).ToList())
            f.Settings.Remove(key);

        f.Start();

        Assert.DoesNotContain(f.Writes, w => w.Key.StartsWith(action, StringComparison.Ordinal));
        f.RunForPlugin.Received(1).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
    }

    [Fact]
    public void ForcedOffActionDeletedByUser_IsNotWrittenAndRuns()
    {
        using var f = new AutoDutyFixture();
        const string retire = $"{AutoDutyFixture.Pre}[RetireLoopActionConfig].Enabled";
        f.Settings.Remove(retire);

        f.Start();

        Assert.DoesNotContain(f.Writes, w => w.Key == retire);
        f.GetConfig.Received(1).InvokeFunc(retire);
        f.RunForPlugin.Received(1).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
    }

    [Fact]
    public void IgnoredSetting_DoesNotRunAndSwitchesBack()
    {
        using var f = new AutoDutyFixture();
        f.GetConfig.InvokeFunc(AutoDutyFixture.ModeKey).Returns("Trust");

        Assert.Throws<TaskException>(f.Start);
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.RunSubmitted);
        f.PollFor(3);

        f.AssertUserProfileRestored();
        Assert.True(f.Ipc.TryConfirmIdle());
        f.RunForPlugin.DidNotReceiveWithAnyArgs().InvokeAction(default!, default, default, default);
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
        f.RunForPlugin.Received(1).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
        f.AssertUserProfileRestored();
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
        f.RunForPlugin.Received(1).InvokeAction(AutoDutyIpc.ProfileName, AutoDutyFixture.TerritoryId, 1, false);
        f.Stopped = true;
        f.PollFor(3);
        Assert.True(f.Ipc.TryConfirmIdle());
        f.AssertUserProfileRestored();
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
        f.AssertUserProfileRestored();
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
        Assert.True(f.State.RequiresReconciliation);
        Assert.False(f.Ipc.IsStopped());
        f.Available = true;
        f.Stopped = false; // a manual run in the reloaded provider
        f.Ipc.CancelOwned();
        f.Stop.DidNotReceive().InvokeAction();
        int writes = f.Writes.Count;
        f.Stopped = true;
        f.PollFor(3);
        // The user's profile was never changed, so there is nothing to restore by hand.
        Assert.True(f.Ipc.TryConfirmIdle());
        Assert.Equal(writes, f.Writes.Count);
        f.AssertUserProfileRestored();
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
    public void NativeCompletionAndExitAreRequired_AndProfileSwitchedBackBeforeRelease()
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
        f.AssertUserProfileRestored();
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
        f.AssertUserProfileRestored();
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
        Assert.Equal("Support", f.ProfileSettings![AutoDutyFixture.ModeKey]);

        f.Game = f.Game with { InDuty = false, Loading = true };
        f.PollFor(30); // A slow loading screen is not a failed Stop request either.
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.RequiresReconciliation);
        f.Exit();
        f.PollFor(3);

        Assert.Equal(completed ? ExternalOperationStatus.Succeeded : ExternalOperationStatus.Failed, f.State.Status);
        Assert.False(f.State.RequiresReconciliation);
        f.AssertUserProfileRestored();
        f.Stop.DidNotReceive().InvokeAction();
        Assert.True(f.State.TryReleaseTerminal(f.State.OperationId));
    }

    [Fact]
    public void FailedSwitchBack_RetainsObligationAndRequiresAcknowledgementAfterRetry()
    {
        using var f = new AutoDutyFixture { RunStarts = false };
        f.Start();
        f.FailRelease = true;
        f.Ipc.CancelOwned();
        f.PollFor(16);
        Assert.True(f.State.HasOperation);
        Assert.True(f.State.RequiresReconciliation);
        Assert.False(f.State.CanConfirmIdle);
        Assert.True(f.ProfileActive);
        f.FailRelease = false;
        f.PollFor(3);
        f.AssertUserProfileRestored();
        Assert.True(f.Ipc.TryConfirmIdle());
    }

    [Fact]
    public void SetterThrowsAfterChangingValue_SwitchesBackWithoutRunning()
    {
        using var f = new AutoDutyFixture { ThrowAfterFirstWrite = true };
        Assert.Throws<TaskException>(f.Start);
        Assert.True(f.State.HasOperation);
        Assert.False(f.State.RunSubmitted);
        f.PollFor(3);
        f.AssertUserProfileRestored();
        Assert.True(f.Ipc.TryConfirmIdle());
        f.RunForPlugin.DidNotReceiveWithAnyArgs().InvokeAction(default!, default, default, default);
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
