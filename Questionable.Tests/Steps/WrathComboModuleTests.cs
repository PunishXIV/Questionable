using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Questionable;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.Model.Common;
using Questionable.Tests.TestData;
using WrathCombo.API;
using WrathCombo.API.Enum;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class WrathComboModuleTests : IDisposable
{
    private readonly Guid _lease = Guid.NewGuid();
    // Wrath's wrapper caches its call gates; this test class resets the shared substitutes per test.
    private static readonly ICallGateSubscriber<bool> _ready = Substitute.For<ICallGateSubscriber<bool>>();
    private static readonly ICallGateSubscriber<Guid, object, object, SetResult> _configure =
        Substitute.For<ICallGateSubscriber<Guid, object, object, SetResult>>();
    private static readonly ICallGateSubscriber<Guid, object> _release = Substitute.For<ICallGateSubscriber<Guid, object>>();
    private static readonly ICallGateSubscriber<string, string, string?, Guid?> _register =
        Substitute.For<ICallGateSubscriber<string, string, string?, Guid?>>();
    private static readonly ICallGateSubscriber<Guid, bool, SetResult> _auto = Substitute.For<ICallGateSubscriber<Guid, bool, SetResult>>();
    private static readonly ICallGateSubscriber<Guid, SetResult> _job = Substitute.For<ICallGateSubscriber<Guid, SetResult>>();
    private readonly IFramework _framework = Substitute.For<IFramework>();
    private readonly ILogger<WrathComboModule> _logger = Substitute.For<ILogger<WrathComboModule>>();
    private readonly WrathComboModule _module;
    private Action<int, string> _cancel = null!;

    public WrathComboModuleTests()
    {
        _ready.ClearSubstitute();
        _configure.ClearSubstitute();
        _release.ClearSubstitute();
        _register.ClearSubstitute();
        _auto.ClearSubstitute();
        _job.ClearSubstitute();
        var plugin = Substitute.For<IDalamudPluginInterface>();
        var callback = Substitute.For<ICallGateProvider<int, string, object>>();
        callback.When(x => x.RegisterAction(Arg.Any<Action<int, string>>()))
            .Do(call => _cancel = call.Arg<Action<int, string>>());
        plugin.GetIpcProvider<int, string, object>("Questionable$Wrath.WrathComboCallback").Returns(callback);
        plugin.GetIpcSubscriber<bool>("WrathCombo.IPCReady").Returns(_ready);
        plugin.GetIpcSubscriber<string, string, string?, Guid?>("WrathCombo.RegisterForLeaseWithCallback").Returns(_register);
        plugin.GetIpcSubscriber<Guid, bool, SetResult>("WrathCombo.SetAutoRotationState").Returns(_auto);
        plugin.GetIpcSubscriber<Guid, SetResult>("WrathCombo.SetCurrentJobAutoRotationReady").Returns(_job);
        plugin.GetIpcSubscriber<Guid, object, object, SetResult>("WrathCombo.SetAutoRotationConfigState").Returns(_configure);
        plugin.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl").Returns(_release);
        _ready.InvokeFunc().Returns(true);
        _register.InvokeFunc("Questionable", "Questionable", "Questionable$Wrath").Returns(_lease);
        _auto.InvokeFunc(_lease, true).Returns(SetResult.Okay);
        _auto.InvokeFunc(_lease, false).Returns(SetResult.Okay);
        _job.InvokeFunc(_lease).Returns(SetResult.Okay);
        _configure.InvokeFunc(_lease, Arg.Any<object>(), Arg.Any<object>()).Returns(SetResult.Okay);
        var configuration = new Configuration();
        configuration.General.CombatModule = ECombatModule.WrathCombo;
        _framework.IsInFrameworkUpdateThread.Returns(true);
        WrathIPCWrapper.Init(plugin);
        _module = new WrathComboModule(_logger, configuration, plugin,
            _framework);
    }

    public void Dispose() => _module.Dispose();

    [Fact]
    public void ItemPause_DisablesRotationWithoutReleasingOrReacquiringLease()
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        Assert.True(_module.Pause());
        _auto.Received(1).InvokeFunc(_lease, false);
        _release.DidNotReceive().InvokeAction(_lease);
        _module.VerifyActive();
        Assert.True(_module.Resume());
        _auto.Received(2).InvokeFunc(_lease, true);
        _register.Received(1).InvokeFunc("Questionable", "Questionable", "Questionable$Wrath");
        Assert.True(_module.Stop());
        _release.Received(1).InvokeAction(_lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedItemTransition_FailsButRetainsLeaseForFinalCleanup(bool resume)
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        if (resume)
            Assert.True(_module.Pause());
        _auto.InvokeFunc(_lease, resume).Returns(SetResult.InvalidConfiguration);
        Assert.False(resume ? _module.Resume() : _module.Pause());
        _release.DidNotReceive().InvokeAction(_lease);
        Assert.True(_module.Stop());
        _release.Received(1).InvokeAction(_lease);
    }

    [Fact]
    public void LeaseCancelledDuringPauseRequest_DoesNotReportSuccessfulPause()
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        _auto.InvokeFunc(_lease, false).Returns(_ =>
        {
            _cancel(0, "cancelled while applying pause");
            return SetResult.Okay;
        });
        Assert.False(_module.Pause());
        Assert.True(_module.Stop());
        _release.DidNotReceive().InvokeAction(_lease);
    }

    [Fact]
    public void CancelledLeaseDuringItemPause_IsNotResumed()
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        Assert.True(_module.Pause());
        _cancel(0, "cancelled during item cast");
        Assert.False(_module.Resume());
        _auto.Received(1).InvokeFunc(_lease, true);
        Assert.True(_module.Stop());
        _release.DidNotReceive().InvokeAction(_lease);
    }

    [Theory]
    [InlineData(AutoRotationConfigOption.DPSRotationMode)]
    [InlineData(AutoRotationConfigOption.HealerRotationMode)]
    [InlineData(AutoRotationConfigOption.AutoRez)]
    [InlineData(AutoRotationConfigOption.AutoRezOutOfParty)]
    public void RejectedConfiguration_FailsStartAndReleasesPartialLease(AutoRotationConfigOption rejected)
    {
        _configure.InvokeFunc(_lease, rejected, Arg.Any<object>()).Returns(SetResult.InvalidConfiguration);
        Assert.False(_module.Start(CombatTestData.Create()));
        _release.Received(1).InvokeAction(_lease);
        Assert.True(_module.Stop());
        _release.Received(1).InvokeAction(_lease);
    }

    [Fact]
    public void ThrowingConfiguration_AlsoReleasesPartialLease()
    {
        _configure.InvokeFunc(_lease, Arg.Any<object>(), Arg.Any<object>())
            .Returns(_ => throw new InvalidOperationException("provider failed"));
        Assert.False(_module.Start(CombatTestData.Create()));
        _release.Received(1).InvokeAction(_lease);
    }

    [Fact]
    public void FailedRelease_RetainsSameLeaseForRetry()
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        bool failed = true;
        _release.When(x => x.InvokeAction(_lease)).Do(_ =>
        {
            if (failed)
                throw new InvalidOperationException("provider unloaded");
        });
        Assert.False(_module.Stop());
        Assert.False(_module.Stop());
        Assert.False(_module.Start(CombatTestData.Create()));
        Assert.Single(_logger.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log" &&
            call.GetArguments()[0] is LogLevel.Warning);
        failed = false;
        Assert.True(_module.Stop());
        _release.Received(3).InvokeAction(_lease);
        Assert.True(_module.Start(CombatTestData.Create()));
        failed = true;
        Assert.False(_module.Stop());
        Assert.Equal(2, _logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log" &&
            call.GetArguments()[0] is LogLevel.Warning));
        failed = false;
    }

    [Fact]
    public void CancelledLease_StopsControllerWithoutReleasingOtherControl()
    {
        using var controller = new CombatController([_module], null!, Substitute.For<ITargetManager>(),
            Substitute.For<IObjectTable>(), Substitute.For<ICondition>(), Substitute.For<IClientState>(),
            null!, null!, _framework, NullLogger<CombatController>.Instance);
        Assert.True(controller.Start(CombatTestData.Create()));
        _cancel(0, "manual takeover");
        Assert.Contains("lease was cancelled", Assert.Throws<TaskException>(() => controller.Update()).Message);
        Assert.False(controller.IsRunning);
        _release.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void ReusedLease_DuplicateResultsStillStart()
    {
        _auto.InvokeFunc(_lease, true).Returns(SetResult.Duplicate);
        _job.InvokeFunc(_lease).Returns(SetResult.Duplicate);
        _configure.InvokeFunc(_lease, Arg.Any<object>(), Arg.Any<object>()).Returns(SetResult.Duplicate);
        Assert.True(_module.Start(CombatTestData.Create()));
        _release.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void MissingProvider_IsCheckedEvenWithoutTarget()
    {
        Assert.True(_module.Start(CombatTestData.Create()));
        _ready.InvokeFunc().Returns(false);
        Assert.Throws<TaskException>(() => _module.VerifyActive());
    }
}
