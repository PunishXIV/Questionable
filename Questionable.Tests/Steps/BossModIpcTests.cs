using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller.Steps;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class BossModIpcTests
{
    private readonly BossModTestContext _context = new();
    private BossModIpc Ipc => _context.Ipc;

    [Fact]
    public void RejectedCreation_DoesNotActivatePreset()
    {
        _context.GetPreset.InvokeFunc(Arg.Any<string>()).Returns((string?)null);
        _context.CreatePreset.InvokeFunc(Arg.Any<string>(), true).Returns(false);

        Assert.Throws<TaskException>(() => Ipc.SetActivePreset(BossModIpc.EPreset.Overworld));

        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        Assert.True(Ipc.StopOwnedPreset());
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Fact]
    public void RejectedActivation_DoesNotReportSuccessOrLoseSettingCleanup()
    {
        _context.RejectActivation = true;
        Assert.Throws<TaskException>(() => Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle));
        Assert.True(Ipc.HasPendingCleanup);
        Assert.True(Ipc.StopOwnedPreset());
        AssertOriginalSettings();
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("User preset")]
    public void Stop_RestoresOriginalPresetAndChangedSettingsOnce(string? previous)
    {
        _context.ActivePreset = previous;
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.Overworld);
        Assert.False(Ipc.HasPendingCleanup);
        Assert.Null(Ipc.CleanupReason);

        Ipc.DisableSoloDutyPreset();
        Assert.Equal(previous, _context.ActivePreset);
        AssertOriginalSettings();
        Assert.False(Ipc.HasOwnedSoloDuty);
        Assert.Equal(2, _context.Writes.Count(x => x.Field == "EnableQuestBattles"));
        Assert.Equal(2, _context.Writes.Count(x => x.Field == "ClearPresetOnDeath"));
        int writes = _context.Writes.Count;
        Ipc.DisableSoloDutyPreset();
        Assert.True(Ipc.StopOwnedPreset());
        Assert.Equal(writes, _context.Writes.Count);
    }

    [Fact]
    public void SettingsAlreadyCorrect_AreNeverWrittenOrRestored()
    {
        _context.Settings[("ZoneModuleConfig", "EnableQuestBattles")] = true;
        _context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")] = false;
        _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")] = false;
        _context.Settings[("AIConfig", "Enabled")] = false;
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        Ipc.DisableSoloDutyPreset();
        Assert.Empty(_context.Writes);
    }

    [Fact]
    public void MovementSetting_IsNeverTouched()
    {
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        Ipc.DisableSoloDutyPreset();
        Assert.DoesNotContain(_context.Writes, x => x.Field == "PreventMovingWhileCasting");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualSettingChanges_AreNotReappliedOnSwitchOrOverwrittenOnStop(bool clearPresetOnDeath)
    {
        _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")] = clearPresetOnDeath;
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.Settings[("ZoneModuleConfig", "EnableQuestBattles")] = false;
        _context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")] = true;
        _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")] = true;
        _context.Settings[("AIConfig", "Enabled")] = true;
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.Overworld);
        Ipc.DisableSoloDutyPreset();
        Assert.Equal(clearPresetOnDeath ? 4 : 3, _context.Writes.Count);
        AssertOriginalSettings();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SoloDeathPolicy_RestoresActualSettingOnStopOrManualTakeover(bool initiallyEnabled, bool manualTakeover)
    {
        _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")] = initiallyEnabled;
        Ipc.StartSoloDuty(454);
        Assert.False(_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.Equal(initiallyEnabled ? 1 : 0, _context.Writes.Count(x => x.Field == "ClearPresetOnDeath"));

        if (manualTakeover)
        {
            _context.ActivePreset = "Manual replacement";
            Assert.Throws<TaskException>(Ipc.PollSoloDuty);
            Assert.False(Ipc.StopOwnedPreset());
            Assert.Equal("Manual replacement", _context.ActivePreset);
        }
        else
        {
            Assert.True(Ipc.StopOwnedPreset());
            Assert.Equal("User preset", _context.ActivePreset);
        }

        Assert.Equal(initiallyEnabled, _context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.Equal(initiallyEnabled ? 2 : 0, _context.Writes.Count(x => x.Field == "ClearPresetOnDeath"));
    }

    [Fact]
    public void NonSoloPreset_DoesNotChangeDeathPolicy()
    {
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);
        Assert.True(_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.True(Ipc.StopOwnedPreset());
        Assert.DoesNotContain(_context.Writes, x => x.Field == "ClearPresetOnDeath");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverworldCombatEnd_KeepsOwnershipUntilStopAndRestoresPolicy(bool initiallyEnabled)
    {
        _context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")] = initiallyEnabled;
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);

        // Model BossMod's normal combat-end transition before QST's next update.
        if (_context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")])
            _context.ActivePreset = null;
        Ipc.VerifyOwnedPreset();
        Assert.Equal("Questionable", _context.ActivePreset);
        Assert.False(Ipc.HasPendingCleanup);

        Assert.True(Ipc.StopOwnedPreset());
        Assert.Equal("User preset", _context.ActivePreset);
        Assert.Equal(initiallyEnabled, _context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")]);
        Assert.Equal(initiallyEnabled ? 2 : 0, _context.Writes.Count(x => x.Field == "ClearPresetOnCombatEnd"));
        Assert.True(_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPresetCleanup_RestoresIndependentSettingsAndRetainsRetry(bool failRead)
    {
        _context.ActivePreset = null;
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.FailReadPreset = failRead;
        _context.FailClearPreset = !failRead;

        Assert.Throws<TaskException>(Ipc.DisableSoloDutyPreset);
        AssertOriginalSettings();
        Assert.True(Ipc.HasPendingCleanup);
        int writes = _context.Writes.Count;
        _context.FailReadPreset = _context.FailClearPreset = false;
        Ipc.PollSoloDuty();
        Assert.False(Ipc.HasPendingCleanup);
        Assert.Equal(writes, _context.Writes.Count);
        Assert.Null(_context.ActivePreset);
    }

    [Fact]
    public void RejectedClear_RetainsCleanupUntilPresetIsActuallyReleased()
    {
        _context.ActivePreset = null;
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);
        _context.ClearPreset.Configure().InvokeFunc().Returns(false);
        Assert.False(Ipc.StopOwnedPreset());
        Assert.True(Ipc.HasPendingCleanup);
        _context.ActivePreset = null;
        Assert.True(Ipc.StopOwnedPreset());
    }

    [Fact]
    public void RejectedPreviousPresetRestore_RetainsCleanup()
    {
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);
        _context.RejectActivation = true;
        Assert.False(Ipc.StopOwnedPreset());
        _context.RejectActivation = false;
        Assert.True(Ipc.StopOwnedPreset());
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Fact]
    public void ThrowAfterActivation_RetainsEnoughOwnershipToRestore()
    {
        _context.AfterActivation = () => throw new InvalidOperationException("lost response");
        Assert.Throws<TaskException>(() => Ipc.SetActivePreset(BossModIpc.EPreset.Overworld));
        Assert.True(Ipc.HasPendingCleanup);
        Assert.Equal("Questionable", _context.ActivePreset);
        _context.AfterActivation = null;
        Assert.True(Ipc.StopOwnedPreset());
        Assert.Equal("User preset", _context.ActivePreset);
    }

    [Fact]
    public void ThrowAfterSettingMutation_RetainsPartialAcquisitionBeforePresetActivation()
    {
        _context.AfterSettingWrite = (_, field, _) =>
        {
            if (field == "EnableQuestBattles")
                throw new InvalidOperationException("lost response");
        };
        Assert.Throws<TaskException>(() => Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle));
        Assert.True(Ipc.HasOwnedSoloDuty);
        Assert.True(Ipc.HasPendingCleanup);
        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        _context.AfterSettingWrite = null;
        Ipc.DisableSoloDutyPreset();
        AssertOriginalSettings();
    }

    [Fact]
    public void FailedSettingRestore_DoesNotDiscardOtherObligations()
    {
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.SettingError = (_, field) => field == "ClearPresetOnCombatEnd" ? "Rejected" : null;
        Assert.False(Ipc.StopOwnedPreset());
        Assert.False(_context.Settings[("ZoneModuleConfig", "EnableQuestBattles")]);
        Assert.True(_context.Settings[("AIConfig", "Enabled")]);
        Assert.Equal("User preset", _context.ActivePreset);
        _context.SettingError = null;
        Assert.True(Ipc.StopOwnedPreset());
        AssertOriginalSettings();
    }

    [Fact]
    public void ConfigurationQuery_UsesNativeContractAndRejectsInvalidReadback()
    {
        _context.ConfigurationEndpoint.Configure().InvokeFunc(
            Arg.Is<IReadOnlyList<string>>(x => x.Count == 2 && x[0] == "ZoneModuleConfig"), false)
            .Returns(["Config type not found. Valid types:"]);
        Assert.Throws<TaskException>(() => Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle));
        Assert.DoesNotContain(_context.Writes, x => x.Field == "EnableQuestBattles");
        Assert.True(Ipc.StopOwnedPreset());
        _context.ConfigurationEndpoint.DidNotReceive().InvokeFunc(
            Arg.Is<IReadOnlyList<string>>(x => x.Count > 0 && x[0] == "cfg"), false);
    }

    [Fact]
    public void DisableWithoutOwnership_DoesNotTouchManualPresetOrConfiguration()
    {
        Ipc.DisableSoloDutyPreset();
        Ipc.Cleanup();
        _context.GetActivePreset.DidNotReceive().InvokeFunc();
        _context.ClearPreset.DidNotReceive().InvokeFunc();
        _context.ConfigurationEndpoint.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default);
    }

    [Fact]
    public void ManualPresetTakeover_IsPreservedAndRequiresExplicitIdleAcknowledgement()
    {
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.ActivePreset = "Manual replacement";
        Assert.Throws<TaskException>(Ipc.PollSoloDuty);
        Assert.False(Ipc.StopOwnedPreset());
        Assert.Equal("Manual replacement", _context.ActivePreset);
        AssertOriginalSettings();
        Assert.False(Ipc.TryConfirmStopped());
        _context.ActivePreset = null;
        Assert.True(Ipc.CanConfirmStopped);
        Assert.True(Ipc.TryConfirmStopped());
        Assert.False(Ipc.HasPendingCleanup);
    }

    [Fact]
    public void ProviderLoss_BlocksNewAcquisitionAndNeverReplaysIntoReplacement()
    {
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.Available = false;
        Assert.Throws<TaskException>(Ipc.PollSoloDuty);
        Assert.False(Ipc.StopOwnedPreset());
        Assert.False(Ipc.CanConfirmStopped);
        _context.Available = true;
        _context.ActivePreset = "Questionable - Quest Battles";
        int writes = _context.Writes.Count;
        _context.SetPreset.ClearReceivedCalls();
        Assert.Throws<TaskException>(() => Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.Overworld));
        Assert.False(Ipc.StopOwnedPreset());
        Assert.Equal(writes, _context.Writes.Count);
        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        _context.ClearPreset.DidNotReceive().InvokeFunc();
        _context.ActivePreset = null;
        Assert.True(Ipc.TryConfirmStopped());
        Assert.Equal(writes, _context.Writes.Count);
    }

    [Fact]
    public void NativeVersionStillRequired_ForReadinessAndAcquisition()
    {
        _context.Provider.Version.Returns((Version)null!);
        Assert.False(Ipc.IsReady());
        Assert.Throws<TaskException>(() => Ipc.SetActivePreset(BossModIpc.EPreset.Overworld));
        Assert.False(Ipc.HasPendingCleanup);
    }

    [Fact]
    public void MultiplePresets_AreNeitherOverwrittenNorConsideredIdle()
    {
        _context.ActivePresetList = ["Manual one", "Manual two"];
        Assert.Throws<TaskException>(() => Ipc.SetActivePreset(BossModIpc.EPreset.Overworld));
        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        _context.ActivePresetList = null;
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);
        _context.Available = false;
        Assert.Throws<TaskException>(Ipc.VerifyOwnedPreset);
        _context.Available = true;
        _context.ActivePreset = null;
        _context.ActivePresetList = ["Manual one", "Manual two"];
        Assert.False(Ipc.CanConfirmStopped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("User preset")]
    public void StopBeforeOwnershipPoll_WithMultiplePresets_RetainsBarrierWithoutClearingManualWork(string? previous)
    {
        _context.ActivePreset = previous;
        Ipc.SetActivePreset(BossModIpc.EPreset.Overworld);
        _context.SetPreset.ClearReceivedCalls();
        _context.ActivePresetList = ["Questionable", "Manual preset"];
        _context.ActivePreset = null; // Native GetActive returns null when the list contains multiple presets.

        Assert.False(Ipc.StopOwnedPreset());
        Assert.True(Ipc.HasPendingCleanup);
        Assert.False(Ipc.CanConfirmStopped);
        Assert.Collection(_context.ActivePresetList,
            preset => Assert.Equal("Questionable", preset),
            preset => Assert.Equal("Manual preset", preset));
        _context.ClearPreset.DidNotReceive().InvokeFunc();
        _context.SetPreset.DidNotReceiveWithAnyArgs().InvokeFunc(default!);
        AssertOriginalSettings();

        _context.ActivePresetList = ["Manual preset"];
        Assert.False(Ipc.TryConfirmStopped());
        _context.ActivePresetList = [];
        Assert.True(Ipc.TryConfirmStopped());
        Assert.False(Ipc.HasPendingCleanup);
    }

    [Fact]
    public void ConfirmationUi_TreatsGenericProviderExceptionsAsUnknown()
    {
        Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        _context.Available = false;
        Assert.Throws<TaskException>(Ipc.PollSoloDuty);
        _context.Available = true;
        _context.GetActivePresets.Configure().InvokeFunc().Returns(_ => throw new InvalidOperationException("provider bug"));
        Assert.False(Ipc.CanConfirmStopped);
        Assert.False(Ipc.TryConfirmStopped());
        Assert.True(Ipc.HasPendingCleanup);
    }

    private void AssertOriginalSettings()
    {
        Assert.False(_context.Settings[("ZoneModuleConfig", "EnableQuestBattles")]);
        Assert.True(_context.Settings[("AutorotationConfig", "ClearPresetOnCombatEnd")]);
        Assert.True(_context.Settings[("AutorotationConfig", "ClearPresetOnDeath")]);
        Assert.True(_context.Settings[("ActionTweaksConfig", "PreventMovingWhileCasting")]);
        Assert.True(_context.Settings[("AIConfig", "Enabled")]);
    }

    internal sealed class BossModTestContext
    {
        public ICallGateSubscriber<string?> GetActivePreset { get; } = Substitute.For<ICallGateSubscriber<string?>>();
        public ICallGateSubscriber<List<string>> GetActivePresets { get; } = Substitute.For<ICallGateSubscriber<List<string>>>();
        public ICallGateSubscriber<string, string?> GetPreset { get; } = Substitute.For<ICallGateSubscriber<string, string?>>();
        public ICallGateSubscriber<string, bool, bool> CreatePreset { get; } = Substitute.For<ICallGateSubscriber<string, bool, bool>>();
        public ICallGateSubscriber<string, bool> SetPreset { get; } = Substitute.For<ICallGateSubscriber<string, bool>>();
        public ICallGateSubscriber<bool> ClearPreset { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>> ConfigurationEndpoint { get; } =
            Substitute.For<ICallGateSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>>>();
        public IExposedPlugin Provider { get; } = Substitute.For<IExposedPlugin>();
        public Configuration Configuration { get; } = new();
        public Dictionary<(string Type, string Field), bool> Settings { get; } = new()
        {
            [("ActionTweaksConfig", "PreventMovingWhileCasting")] = true,
            [("AutorotationConfig", "ClearPresetOnCombatEnd")] = true,
            [("AutorotationConfig", "ClearPresetOnDeath")] = true,
            [("ZoneModuleConfig", "EnableQuestBattles")] = false,
            [("AIConfig", "Enabled")] = true,
        };
        public List<(string Type, string Field, bool Value)> Writes { get; } = [];
        public string? ActivePreset { get; set; } = "User preset";
        public List<string>? ActivePresetList { get; set; }
        public bool Available { get; set; } = true;
        public bool RejectActivation { get; set; }
        public bool FailReadPreset { get; set; }
        public bool FailClearPreset { get; set; }
        public Action? AfterActivation { get; set; }
        public Action<string, string, bool>? AfterSettingWrite { get; set; }
        public Func<string, string, string?>? SettingError { get; set; }
        public BossModIpc Ipc { get; }

        public BossModTestContext()
        {
            var plugin = Substitute.For<IDalamudPluginInterface>();
            Provider.InternalName.Returns("BossMod");
            Provider.IsLoaded.Returns(_ => Available);
            Provider.Version.Returns(new Version(1, 0));
            plugin.InstalledPlugins.Returns([Provider]);
            plugin.GetIpcSubscriber<string?>("BossMod.Presets.GetActive").Returns(GetActivePreset);
            plugin.GetIpcSubscriber<List<string>>("BossMod.Presets.GetActiveList").Returns(GetActivePresets);
            plugin.GetIpcSubscriber<string, string?>("BossMod.Presets.Get").Returns(GetPreset);
            plugin.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create").Returns(CreatePreset);
            plugin.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").Returns(SetPreset);
            plugin.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive").Returns(ClearPreset);
            plugin.GetIpcSubscriber<string, bool>("BossMod.Presets.Delete").Returns(Substitute.For<ICallGateSubscriber<string, bool>>());
            plugin.GetIpcSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>>("BossMod.Configuration").Returns(ConfigurationEndpoint);
            GetActivePreset.HasFunction.Returns(_ => Available);
            SetPreset.HasFunction.Returns(_ => Available);
            ClearPreset.HasFunction.Returns(_ => Available);
            GetActivePresets.HasFunction.Returns(_ => Available);
            ConfigurationEndpoint.HasFunction.Returns(_ => Available);
            GetPreset.HasFunction.Returns(_ => Available);
            GetPreset.InvokeFunc(Arg.Any<string>()).Returns("{}");
            CreatePreset.InvokeFunc(Arg.Any<string>(), true).Returns(true);
            GetActivePresets.InvokeFunc().Returns(_ => ActivePresetList ?? (ActivePreset == null ? [] : [ActivePreset]));
            GetActivePreset.InvokeFunc().Returns(_ => FailReadPreset ? throw new InvalidOperationException("read failed") : ActivePreset);
            SetPreset.InvokeFunc(Arg.Any<string>()).Returns(call =>
            {
                if (RejectActivation)
                    return false;
                ActivePreset = call.Arg<string>();
                AfterActivation?.Invoke();
                return true;
            });
            ClearPreset.InvokeFunc().Returns(_ =>
            {
                if (FailClearPreset)
                    throw new InvalidOperationException("clear failed");
                ActivePreset = null;
                return true;
            });
            ConfigurationEndpoint.InvokeFunc(Arg.Any<IReadOnlyList<string>>(), false).Returns(call =>
            {
                var args = call.Arg<IReadOnlyList<string>>();
                if (args.Count is < 2 or > 3 || !Settings.TryGetValue((args[0], args[1]), out bool previous))
                    return ["Config type or field not found"];
                if (args.Count == 2)
                    return new[] { previous.ToString() };
                if (SettingError?.Invoke(args[0], args[1]) is { } error)
                    return new[] { error };
                bool value = bool.Parse(args[2]);
                Settings[(args[0], args[1])] = value;
                Writes.Add((args[0], args[1], value));
                AfterSettingWrite?.Invoke(args[0], args[1], value);
                return Array.Empty<string>();
            });
            Ipc = new BossModIpc(plugin, Configuration, null!, Substitute.For<ICommandManager>());
        }
    }
}
