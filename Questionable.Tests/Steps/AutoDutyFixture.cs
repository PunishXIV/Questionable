using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Questionable.Data;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

internal sealed class AutoDutyFixture : IDisposable
{
    public const uint CfcId = 42;
    public const uint TerritoryId = 101;
    public const string ModeKey = "Meta.DutyModeEnum";
    public const string UnsyncedKey = "Meta.Unsynced";
    public static readonly TerritoryData.ContentFinderConditionData Content = new(CfcId, "Test duty", TerritoryId, 100, 50, null);
    private readonly TestClock _clock = new();
    private readonly IDutyState _dutyState = Substitute.For<IDutyState>();
    public Configuration Configuration { get; } = new();
    public AutoDutyOperationState State { get; }
    public AutoDutyIpc Ipc { get; }
    public ICallGateSubscriber<bool> StoppedGate { get; } = Substitute.For<ICallGateSubscriber<bool>>();
    public ICallGateSubscriber<uint, int, bool, object> Run { get; } = Substitute.For<ICallGateSubscriber<uint, int, bool, object>>();
    public ICallGateSubscriber<string, uint, int, bool, object> RunForPlugin { get; } = Substitute.For<ICallGateSubscriber<string, uint, int, bool, object>>();
    public ICallGateSubscriber<string, bool, object> PluginConfig { get; } = Substitute.For<ICallGateSubscriber<string, bool, object>>();
    public ICallGateSubscriber<object> Stop { get; } = Substitute.For<ICallGateSubscriber<object>>();
    public ICallGateSubscriber<string, string> GetConfig { get; } = Substitute.For<ICallGateSubscriber<string, string>>();
    public ICallGateSubscriber<bool> PathRunning { get; } = Substitute.For<ICallGateSubscriber<bool>>();
    // The user's own AutoDuty profile, and Questionable's (a copy of it, made on first use).
    public Dictionary<string, string> Settings { get; } = new() { [ModeKey] = "Trust", [UnsyncedKey] = "True" };
    public Dictionary<string, string>? ProfileSettings { get; private set; }
    public bool ProfileActive { get; set; }
    public List<(string Key, string Value)> Writes { get; } = [];
    public List<string> Calls { get; } = [];
    public AutoDutyGameState Game { get; set; } = new(true, 1, false, false, false, true);
    public bool Available { get; set; } = true;
    public bool RunStarts { get; set; } = true;
    public bool ThrowRun { get; set; }
    public bool ThrowStop { get; set; }
    public bool FailRelease { get; set; }
    public bool ThrowAfterFirstWrite { get; set; }
    private bool _pluginRun;
    private bool _stopped = true;

    public bool Stopped
    {
        get => _stopped;
        set
        {
            _stopped = value;
            // Like AutoDuty, a run started for a plugin switches back to the user's profile when it stops.
            if (value && _pluginRun)
            {
                _pluginRun = false;
                ProfileActive = false;
            }
        }
    }

    private Dictionary<string, string> ActiveSettings => ProfileActive ? ProfileSettings! : Settings;

    public AutoDutyFixture()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        var stopped = StoppedGate;
        var path = Substitute.For<ICallGateSubscriber<uint, bool>>();
        var setConfig = Substitute.For<ICallGateSubscriber<string, object, object>>();
        plugin.GetIpcSubscriber<bool>("AutoDuty.IsStopped").Returns(stopped);
        plugin.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath").Returns(path);
        plugin.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run").Returns(Run);
        plugin.GetIpcSubscriber<string, uint, int, bool, object>("AutoDuty.RunForPlugin").Returns(RunForPlugin);
        plugin.GetIpcSubscriber<string, bool, object>("AutoDuty.PluginConfig").Returns(PluginConfig);
        plugin.GetIpcSubscriber<object>("AutoDuty.Stop").Returns(Stop);
        plugin.GetIpcSubscriber<string, string>("AutoDuty.GetConfig").Returns(GetConfig);
        plugin.GetIpcSubscriber<string, object, object>("AutoDuty.SetConfig").Returns(setConfig);
        plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").Returns(PathRunning);
        plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress").Returns(Substitute.For<ICallGateSubscriber<bool>>());
        plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").Returns(Substitute.For<ICallGateSubscriber<bool>>());
        stopped.HasFunction.Returns(_ => Available);
        stopped.InvokeFunc().Returns(_ => Stopped);
        path.HasFunction.Returns(true);
        path.InvokeFunc(TerritoryId).Returns(true);
        Run.HasAction.Returns(_ => Available);
        RunForPlugin.HasAction.Returns(_ => Available);
        PluginConfig.HasAction.Returns(_ => Available);
        Stop.HasAction.Returns(_ => Available);
        GetConfig.HasFunction.Returns(true);
        setConfig.HasAction.Returns(true);
        GetConfig.InvokeFunc(Arg.Any<string>()).Returns(x => ActiveSettings[x.Arg<string>()]);
        setConfig.When(x => x.InvokeAction(Arg.Any<string>(), Arg.Any<object>())).Do(x =>
        {
            string key = x.ArgAt<string>(0);
            string value = (string)x.ArgAt<object>(1);
            Calls.Add($"set {key}");
            ActiveSettings[key] = value;
            Writes.Add((key, value));
            if (ThrowAfterFirstWrite && Writes.Count == 1)
                throw new InvalidOperationException("Reply lost after setting changed");
        });
        PluginConfig.When(x => x.InvokeAction(Arg.Any<string>(), Arg.Any<bool>())).Do(x =>
        {
            Assert.Equal(AutoDutyIpc.ProfileName, x.ArgAt<string>(0));
            bool on = x.ArgAt<bool>(1);
            Calls.Add(on ? "profile on" : "profile off");
            if (!on && FailRelease)
                throw new InvalidOperationException("Profile switch failed");
            if (on)
                ProfileSettings ??= new(Settings);
            ProfileActive = on;
        });
        RunForPlugin.When(x => x.InvokeAction(AutoDutyIpc.ProfileName, TerritoryId, 1, true)).Do(_ =>
        {
            Assert.True(State!.HasOperation);
            Calls.Add("run");
            ProfileSettings ??= new(Settings);
            ProfileActive = _pluginRun = true;
            if (RunStarts)
                Stopped = false;
            if (ThrowRun)
                throw new InvalidOperationException("Run reply lost");
        });
        Stop.When(x => x.InvokeAction()).Do(_ =>
        {
            if (ThrowStop)
                throw new InvalidOperationException("Stop reply lost");
        });
        Configuration.Duties.RunInstancedContentWithAutoDuty = true;
        Configuration.Duties.RunUnsynced = false;
        State = new AutoDutyOperationState(_clock);
        Ipc = new AutoDutyIpc(plugin, Configuration, id => id == CfcId ? Content : null,
            NullLogger<AutoDutyIpc>.Instance, State, () => Game, _dutyState);
    }

    public void Start() => Ipc.StartInstance(CfcId, AutoDutyIpc.DutyMode.Support);
    public void Advance(TimeSpan duration) { _clock.Advance(duration); Ipc.PollOperation(); }
    public void PollFor(int seconds)
    {
        Ipc.PollOperation();
        for (int i = 0; i < seconds * 2; ++i)
            Advance(TimeSpan.FromMilliseconds(500));
    }
    public void Enter() { Game = new(true, TerritoryId, false, false, true, false); Ipc.PollOperation(); }
    public void Exit() { Game = new(true, 1, false, false, false, true); Ipc.PollOperation(); }
    public void RaiseCompleted() => _dutyState.DutyCompleted += Raise.Event<IDutyState.DutyCompletedDelegate>(new object[] { null! });
    public void AssertUserProfileRestored()
    {
        Assert.False(ProfileActive);
        Assert.Equal("Trust", Settings[ModeKey]);
        Assert.Equal("True", Settings[UnsyncedKey]);
    }
    public void Dispose() => Ipc.Dispose();
}
