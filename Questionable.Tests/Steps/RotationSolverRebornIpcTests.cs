using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.External;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Mode = Questionable.External.RotationSolverRebornIpc.StateCommandType;

namespace Questionable.Tests.Steps;

public sealed class RotationSolverRebornIpcTests
{
    private readonly ICallGateSubscriber<Mode, object> _change = Substitute.For<ICallGateSubscriber<Mode, object>>();
    private readonly ICallGateSubscriber<string, object> _test = Substitute.For<ICallGateSubscriber<string, object>>();
    private readonly IFramework _framework = Substitute.For<IFramework>();
    private readonly Configuration _configuration = new();
    private readonly RotationSolverRebornIpc _ipc;
    private object? _provider = new();
    private Mode _mode;
    private bool _reject;
    private bool _throwAfterChange;
    private bool _failReadingMode;
    private long _ticks;
    private int _providerReads;
    private int _modeReads;

    public RotationSolverRebornIpcTests()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.GetIpcSubscriber<Mode, object>("RotationSolverReborn.ChangeOperatingMode").Returns(_change);
        plugin.GetIpcSubscriber<string, object>("RotationSolverReborn.Test")
            .Returns(_test);
        _change.HasAction.Returns(true);
        _change.When(x => x.InvokeAction(Arg.Any<Mode>())).Do(call =>
        {
            if (!_reject)
                _mode = call.Arg<Mode>();
            if (_throwAfterChange)
                throw new InvalidOperationException("reply failed after applying mode");
        });
        _framework.IsInFrameworkUpdateThread.Returns(true);
        _configuration.General.CombatModule = ECombatModule.RotationSolverReborn;
        _ipc = new RotationSolverRebornIpc(plugin, _configuration, NullLogger<RotationSolverRebornIpc>.Instance,
            _framework, () => { ++_providerReads; return _provider; }, _ =>
            {
                ++_modeReads;
                return _failReadingMode ? throw new InvalidOperationException("mode unavailable") : _mode;
            }, () => _ticks);
    }

    [Theory]
    [InlineData((int)Mode.Auto, false)]
    [InlineData((int)Mode.Manual, false)]
    [InlineData((int)Mode.Auto, true)]
    public void CombatItemPause_SuppressesRotationAndRestoresOriginalModeOnlyOnStop(int originalValue, bool stopWhilePaused)
    {
        Mode original = (Mode)originalValue;
        _mode = original;
        var item = CreateItemModule();
        item.StopRotation();
        Assert.Equal(Mode.Off, _mode);
        item.VerifyActive();
        if (!stopWhilePaused)
        {
            item.StartRotation();
            Assert.Equal(Mode.Henched, _mode);
            item.StopRotation();
            Assert.Equal(Mode.Off, _mode);
            item.StartRotation();
        }
        Assert.True(item.Stop());
        Assert.Equal(original, _mode);
        _change.Received(1).InvokeAction(original);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ItemTransitionFailure_RestoresOriginalMode(bool resume, bool appliedBeforeThrow)
    {
        _mode = Mode.Manual;
        var item = CreateItemModule();
        if (resume)
            item.StopRotation();
        _reject = !appliedBeforeThrow;
        _throwAfterChange = appliedBeforeThrow;
        Assert.Throws<TaskException>(() =>
        {
            if (resume)
                item.StartRotation();
            else
                item.StopRotation();
        });
        _reject = _throwAfterChange = false;
        Assert.True(item.Stop());
        Assert.Equal(Mode.Manual, _mode);
    }

    [Fact]
    public void ManualTakeoverDuringItemPause_IsDetectedAndPreserved()
    {
        _mode = Mode.Auto;
        var item = CreateItemModule();
        item.StopRotation();
        _mode = Mode.Manual;
        Assert.Throws<TaskException>(item.VerifyActive);
        Assert.Throws<TaskException>(item.StartRotation);
        Assert.True(item.Stop());
        Assert.Equal(Mode.Manual, _mode);
    }

    private ItemUseModule CreateItemModule()
    {
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IEnumerable<ICombatModule>))
            .Returns(new ICombatModule[] { new RotationSolverRebornModule(_ipc) });
        var item = new ItemUseModule(services, Substitute.For<ICondition>(), null!, NullLogger<ItemUseModule>.Instance);
        var data = CombatTestData.Create(itemUse: new CombatItemUse { ItemId = 1 });
        Assert.True(item.CanHandleFight(data));
        Assert.True(item.Start(data));
        return item;
    }

    [Theory]
    [InlineData((int)Mode.Off)]
    [InlineData((int)Mode.Auto)]
    [InlineData((int)Mode.Manual)]
    [InlineData((int)Mode.TargetOnly)]
    public void PreferenceChange_RestoresOriginalProviderMode(int originalValue)
    {
        Mode original = (Mode)originalValue;
        _mode = original;
        Assert.True(_ipc.RotationAuto());
        Assert.Equal(Mode.Henched, _mode);
        _configuration.General.CombatModule = ECombatModule.WrathCombo;
        Assert.True(_ipc.RotationStop());
        Assert.Equal(original, _mode);
        _change.Received(1).InvokeAction(original);
        Assert.True(_ipc.RotationStop());
        _change.Received(1).InvokeAction(original);
    }

    [Theory]
    [InlineData((int)Mode.Henched)]
    [InlineData((int)Mode.AutoDuty)]
    public void OtherPluginControl_IsNotOverwrittenOrStopped(int originalValue)
    {
        Mode original = (Mode)originalValue;
        _mode = original;
        Assert.False(_ipc.RotationAuto());
        Assert.True(_ipc.RotationStop());
        _change.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void MissingProvider_DoesNotClaimSuccessfulStart()
    {
        _provider = null;
        Assert.False(_ipc.IsEnabled);
        Assert.False(_ipc.RotationAuto());
        _change.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void UnreadableMode_IsRejectedBeforeChangingProvider()
    {
        _failReadingMode = true;
        Assert.False(_ipc.RotationAuto());
        _change.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void IgnoredStart_IsNotReportedAsAccepted()
    {
        _reject = true;
        Assert.False(_ipc.RotationAuto());
        Assert.True(_ipc.RotationStop());
        _change.DidNotReceive().InvokeAction(Mode.Off);
    }

    [Fact]
    public void StartError_RollsBackMode()
    {
        _mode = Mode.Manual;
        _throwAfterChange = true;
        Assert.False(_ipc.RotationAuto());
        _throwAfterChange = false;
        Assert.True(_ipc.RotationStop());
        Assert.Equal(Mode.Manual, _mode);
    }

    [Fact]
    public void RejectedStop_IsRetriedRegardlessOfPreference()
    {
        Assert.True(_ipc.RotationAuto());
        _configuration.General.CombatModule = ECombatModule.WrathCombo;
        _reject = true;
        Assert.False(_ipc.RotationStop());
        Assert.False(_ipc.RotationAuto());
        _reject = false;
        Assert.True(_ipc.RotationStop());
        Assert.Equal(Mode.Off, _mode);
    }

    [Fact]
    public void ManualModeChange_IsPreservedOnStop()
    {
        Assert.True(_ipc.RotationAuto());
        _mode = Mode.Manual;
        Assert.Throws<TaskException>(() => _ipc.VerifyActive());
        Assert.True(_ipc.RotationStop());
        Assert.Equal(Mode.Manual, _mode);
        _change.DidNotReceive().InvokeAction(Mode.Off);
    }

    [Fact]
    public void ProviderLossAndReplacement_NeverStopNewInstance()
    {
        Assert.True(_ipc.RotationAuto());
        _provider = null;
        Assert.Throws<TaskException>(() => _ipc.VerifyActive());
        Assert.False(_ipc.RotationStop());
        _provider = new();
        Assert.True(_ipc.RotationStop());
        _change.DidNotReceive().InvokeAction(Mode.Off);
    }

    [Fact]
    public void ReturningProvider_DoesNotStopManualRun()
    {
        object? previous = _provider;
        Assert.True(_ipc.RotationAuto());
        _provider = null;
        Assert.False(_ipc.RotationStop());
        _provider = previous;
        Assert.False(_ipc.RotationStop());
        _mode = Mode.Off;
        Assert.True(_ipc.RotationStop());
        _change.DidNotReceive().InvokeAction(Mode.Off);
    }

    [Fact]
    public void FrameworkUnloading_DoesNotReportUnexecutedStartAsSuccessful()
    {
        _framework.IsInFrameworkUpdateThread.Returns(false);
        _framework.IsFrameworkUnloading.Returns(true);
        Assert.False(_ipc.RotationAuto());
        _change.DidNotReceiveWithAnyArgs().InvokeAction(default);
    }

    [Fact]
    public void ActiveFrames_DoNotRepeatLoggingProbeOrReflectionUntilPollInterval()
    {
        Assert.True(_ipc.IsReady());
        _test.DidNotReceiveWithAnyArgs().InvokeAction(default!);
        Assert.True(_ipc.RotationAuto());
        _providerReads = _modeReads = 0;
        for (int i = 0; i < 100; ++i)
            _ipc.VerifyActive();
        Assert.Equal(1, _providerReads);
        Assert.Equal(1, _modeReads);
        _test.Received(1).InvokeAction(Arg.Any<string>());
        _ticks = 250;
        _ipc.VerifyActive();
        Assert.Equal(2, _providerReads);
        Assert.Equal(2, _modeReads);
        _test.Received(1).InvokeAction(Arg.Any<string>());
    }

    [Fact]
    public void MissingEndpoint_FailsImmediatelyInsidePollInterval()
    {
        Assert.True(_ipc.RotationAuto());
        _ipc.VerifyActive();
        _change.HasAction.Returns(false);
        Assert.Throws<TaskException>(() => _ipc.VerifyActive());
    }

    [Fact]
    public void OffThreadStop_DoesNotQueueOrWaitAndFrameworkRetryRestoresMode()
    {
        Assert.True(_ipc.RotationAuto());
        _framework.IsInFrameworkUpdateThread.Returns(false);
        Assert.False(_ipc.RotationStop());
        Assert.False(_ipc.RotationAuto());
        Assert.DoesNotContain(_framework.ReceivedCalls(), x => x.GetMethodInfo().Name == "RunOnFrameworkThread");
        _change.DidNotReceive().InvokeAction(Mode.Off);
        _framework.IsInFrameworkUpdateThread.Returns(true);
        Assert.True(_ipc.RotationStop());
        Assert.Equal(Mode.Off, _mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeReader_SupportsMergedAndReferencedBasicAssembly(bool separateBasic)
    {
        var context = new AssemblyLoadContext($"RSR shape {Guid.NewGuid()}", isCollectible: true);
        try
        {
            using var scope = context.EnterContextualReflection();
            var plugin = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"RotationSolver{Guid.NewGuid():N}"),
                AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Plugin");
            ModuleBuilder stateModule = separateBasic
                ? AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver.Basic"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Basic")
                : plugin;
            Type state = DefineModeState(stateModule);
            object provider = DefineProvider(plugin, separateBasic ? state : null);
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly));
            Assert.Equal(separateBasic, state.Assembly != provider.GetType().Assembly);
            if (separateBasic)
                Assert.Contains(provider.GetType().Assembly.GetReferencedAssemblies(), x => x.Name == "RotationSolver.Basic");
            Assert.Same(state, RotationSolverRebornIpc.ResolveModeType(provider.GetType().Assembly));

            foreach (Mode mode in new[] { Mode.Off, Mode.Auto, Mode.Manual, Mode.TargetOnly, Mode.Henched, Mode.AutoDuty })
            {
                Set("State", mode != Mode.Off);
                Set("IsManual", mode is Mode.Manual or Mode.Henched);
                Set("IsHenched", mode == Mode.Henched);
                Set("IsAutoDuty", mode == Mode.AutoDuty);
                Set("IsTargetOnly", mode == Mode.TargetOnly);
                Assert.Equal(mode, RotationSolverRebornIpc.ReadMode(provider));
            }
            Set("IsPvPStateEnabled", true);
            Assert.Throws<TaskException>(() => RotationSolverRebornIpc.ReadMode(provider));

            void Set(string name, bool value) => state.GetProperty(name)!.SetValue(null, value);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ModeReader_DoesNotUseBasicFromAnotherProviderLoadContext()
    {
        var oldContext = new AssemblyLoadContext($"Old RSR {Guid.NewGuid()}", isCollectible: true);
        var newContext = new AssemblyLoadContext($"New RSR {Guid.NewGuid()}", isCollectible: true);
        try
        {
            Type oldState;
            using (oldContext.EnterContextualReflection())
                oldState = DefineModeState(AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver.Basic"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Basic"));
            using (newContext.EnterContextualReflection())
            {
                object provider = DefineProvider(AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"RotationSolver{Guid.NewGuid():N}"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Plugin"), oldState);
                Assert.DoesNotContain(newContext.Assemblies, x => x == oldState.Assembly);
                Assert.Throws<TaskException>(() => RotationSolverRebornIpc.ReadMode(provider));
            }
        }
        finally
        {
            oldContext.Unload();
            newContext.Unload();
        }
    }

    private static object DefineProvider(ModuleBuilder module, Type? reference)
    {
        TypeBuilder type = module.DefineType("RotationSolver.RotationSolverPlugin", TypeAttributes.Public);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        if (reference != null)
            type.DefineField("BasicReference", reference, FieldAttributes.Public);
        return Activator.CreateInstance(type.CreateType()!)!;
    }

    private static Type DefineModeState(ModuleBuilder module)
    {
        // Match the verified provider shape: an internal static class with public static bool properties.
        TypeBuilder type = module.DefineType("RotationSolver.Basic.DataCenter",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (string name in new[] { "State", "IsManual", "IsHenched", "IsAutoDuty", "IsTargetOnly", "IsPvPStateEnabled" })
        {
            FieldBuilder field = type.DefineField($"_{name}", typeof(bool), FieldAttributes.Private | FieldAttributes.Static);
            PropertyBuilder property = type.DefineProperty(name, PropertyAttributes.None, typeof(bool), Type.EmptyTypes);
            const MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName;
            MethodBuilder getter = type.DefineMethod($"get_{name}", attributes, typeof(bool), Type.EmptyTypes);
            ILGenerator get = getter.GetILGenerator();
            get.Emit(OpCodes.Ldsfld, field);
            get.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            MethodBuilder setter = type.DefineMethod($"set_{name}", attributes, typeof(void), [typeof(bool)]);
            ILGenerator set = setter.GetILGenerator();
            set.Emit(OpCodes.Ldarg_0);
            set.Emit(OpCodes.Stsfld, field);
            set.Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
        }
        return type.CreateType()!;
    }
}
