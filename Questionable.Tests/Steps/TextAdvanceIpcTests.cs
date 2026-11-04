using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.External;
using Xunit;
using TerritoryConfig = Questionable.External.TextAdvanceIpc.ExternalTerritoryConfig;

namespace Questionable.Tests.Steps;

public sealed class TextAdvanceIpcTests : IDisposable
{
    private readonly IFramework _framework = Substitute.For<IFramework>();
    private readonly Configuration _configuration = new();
    private readonly QuestController _controller;
    private readonly TextAdvanceIpc _ipc;
    private readonly ICallGateSubscriber<string, TerritoryConfig, bool> _enable =
        Substitute.For<ICallGateSubscriber<string, TerritoryConfig, bool>>();
    private readonly ICallGateSubscriber<string, bool> _disable =
        Substitute.For<ICallGateSubscriber<string, bool>>();
    private readonly ICallGateSubscriber<bool> _controlled = Substitute.For<ICallGateSubscriber<bool>>();
    private string? _owner;
    private TerritoryConfig? _settings;
    private int _enableCalls;
    private int _disableCalls;
    private int _failure;

    // The user's normal TextAdvance settings permit auto-interaction. QST's
    // external configuration must suppress it continuously while questing.
    private bool AutoInteract => _settings?.EnableAutoInteract ?? true;

    public TextAdvanceIpcTests()
    {
        _controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_controller, new TaskQueue(NullLogger.Instance));
        typeof(QuestController).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_controller, NullLogger<QuestController>.Instance);
        _controller.AutomationType = QuestController.EAutomationType.Automatic;
        _framework.IsInFrameworkUpdateThread.Returns(true);
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.InternalName.Returns("Questionable");
        plugin.GetIpcSubscriber<bool>("TextAdvance.IsInExternalControl").Returns(_controlled);
        plugin.GetIpcSubscriber<string, TerritoryConfig, bool>("TextAdvance.EnableExternalControl").Returns(_enable);
        plugin.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl").Returns(_disable);
        _controlled.InvokeFunc().Returns(_ => _owner != null);
        _enable.InvokeFunc(Arg.Any<string>(), Arg.Any<TerritoryConfig>()).Returns(call =>
        {
            _enableCalls++;
            string requester = call.Arg<string>();
            if (_failure == 1 || (_owner != null && _owner != requester))
                return false;
            if (_failure == 2)
                throw new TargetInvocationException(new InvalidOperationException("Provider unavailable"));
            _owner = requester;
            _settings = call.Arg<TerritoryConfig>();
            if (_failure == 3)
                throw new TargetInvocationException(new InvalidOperationException("Reply lost after applying"));
            return true;
        });
        _disable.InvokeFunc(Arg.Any<string>()).Returns(call =>
        {
            _disableCalls++;
            if (_owner != call.Arg<string>())
                return false;
            _owner = null;
            _settings = null;
            return true;
        });
        _ipc = new(plugin, _framework, _controller, _configuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingCutscenePreference_UpdatesWithoutReleasingControl(bool initialDontSkip)
    {
        _configuration.General.DontSkipCutscenes = initialDontSkip;
        Tick();
        AssertSettings(initialDontSkip);

        _configuration.General.DontSkipCutscenes = !initialDontSkip;
        Tick();

        AssertSettings(!initialDontSkip);
        Assert.Equal(2, _enableCalls);
        Assert.Equal(0, _disableCalls);
        Tick();
        Assert.Equal(2, _enableCalls);
    }

    [Fact]
    public void ProfileSwitch_RefreshesCutsceneSettingsWithoutRestoringAutoInteract()
    {
        _configuration.CreateProfile("Watch cutscenes");
        _configuration.ActivateProfile("Watch cutscenes");
        _configuration.General.DontSkipCutscenes = true;
        _configuration.ClearActiveProfile();
        Tick();

        _configuration.ActivateProfile("Watch cutscenes");
        Tick();
        AssertSettings(dontSkip: true);
        _configuration.ClearActiveProfile();
        Tick();
        AssertSettings(dontSkip: false);
        Assert.Equal(0, _disableCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedRefresh_KeepsControlAndRetriesUntilAcknowledged(int failure)
    {
        Tick();
        _configuration.General.DontSkipCutscenes = true;
        _failure = failure;

        Tick();

        AssertSettings(dontSkip: failure == 3);
        Assert.Equal(2, _enableCalls);
        Assert.Equal(0, _disableCalls);
        _failure = 0;
        Tick();
        AssertSettings(dontSkip: true);
        Assert.Equal(3, _enableCalls);
        Tick();
        Assert.Equal(3, _enableCalls);
        Assert.Equal(0, _disableCalls);
    }

    [Fact]
    public void UnchangedSettings_DoNotResubmit()
    {
        Tick();
        Tick();
        Tick();

        AssertSettings(dontSkip: false);
        Assert.Equal(1, _enableCalls);
        Assert.Equal(0, _disableCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoppingOrDisablingIntegration_ReleasesControl(bool disableIntegration)
    {
        Tick();
        _configuration.General.DontSkipCutscenes = true;
        if (disableIntegration)
            _configuration.General.ConfigureTextAdvance = false;
        else
            _controller.AutomationType = QuestController.EAutomationType.Manual;

        Tick();
        Tick();

        Assert.Null(_owner);
        Assert.True(AutoInteract);
        Assert.Equal(1, _disableCalls);
        Assert.Equal(1, _enableCalls);
    }

    [Fact]
    public void AnotherRequester_IsNotTakenOver()
    {
        _owner = "OtherPlugin";
        Tick();
        _configuration.General.DontSkipCutscenes = true;
        Tick();

        Assert.Equal("OtherPlugin", _owner);
        Assert.Equal(0, _enableCalls);
        Assert.Equal(0, _disableCalls);
    }

    [Fact]
    public void Dispose_ReleasesControlAndUnsubscribesFromUpdates()
    {
        Tick();
        _ipc.Dispose();
        _configuration.General.DontSkipCutscenes = true;
        Tick();

        Assert.Null(_owner);
        Assert.True(AutoInteract);
        Assert.Equal(1, _enableCalls);
        Assert.Equal(1, _disableCalls);
    }

    private void AssertSettings(bool dontSkip)
    {
        Assert.Equal("Questionable", _owner);
        Assert.NotNull(_settings);
        Assert.False(AutoInteract);
        Assert.Equal(!dontSkip, _settings.EnableCutsceneEsc);
        Assert.Equal(!dontSkip, _settings.EnableCutsceneSkipConfirm);
        Assert.Equal(!dontSkip, _settings.EnableTalkSkip);
        Assert.True(_settings.EnableQuestAccept);
        Assert.True(_settings.EnableQuestComplete);
        Assert.True(_settings.EnableRewardPick);
        Assert.True(_settings.EnableRequestHandin);
        Assert.True(_settings.EnableRequestFill);
    }

    private void Tick() => _framework.Update += Raise.Event<IFramework.OnUpdateDelegate>(_framework);

    public void Dispose() => _ipc.Dispose();
}
