using System.Reflection;
using Dalamud.Plugin.Services;
using NSubstitute;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class BossModIpcTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly BossModIpc _ipc;
    private readonly Dictionary<string, string> _presets = [];
    private readonly List<string> _active = [];
    private readonly List<string> _created = [];
    private readonly List<string> _deleted = [];

    public BossModIpcTests()
    {
        NoPluginsInstalled.Ensure();
        _ipc = new(new Configuration(), null!, Substitute.For<ICommandManager>());
        Set("_getPreset", new Func<string, string?>(name => _presets.GetValueOrDefault(name)));
        Set("_createPreset", new Func<string, bool, bool>((content, _) =>
        {
            string name = content.Contains("Quest Battles") ? "Questionable - Quest Battles" : "Questionable";
            _presets[name] = content;
            _created.Add(name);
            return true;
        }));
        Set("_deletePreset", new Func<string, bool>(name =>
        {
            _deleted.Add(name);
            return _presets.Remove(name);
        }));
        Set("_getActivePresets", new Func<List<string>>(() => [.. _active]));
    }

    [Fact]
    public void FirstUse_OverwritesLeftover()
    {
        _presets["Questionable"] = "edited by the user";

        Add(BossModIpc.EPreset.Overworld);
        Add(BossModIpc.EPreset.Overworld);

        Assert.Equal(["Questionable"], _created);
        Assert.NotEqual("edited by the user", _presets["Questionable"]);
    }

    [Fact]
    public void MissingPreset_IsCreatedAgain()
    {
        Add(BossModIpc.EPreset.Overworld);
        _presets.Remove("Questionable");

        Add(BossModIpc.EPreset.Overworld);

        Assert.Equal(["Questionable", "Questionable"], _created);
    }

    [Fact]
    public void Remove_KeepsActivePreset()
    {
        Add(BossModIpc.EPreset.Overworld);
        Add(BossModIpc.EPreset.QuestBattle);
        _active.Add("Questionable");

        _ipc.RemovePresets();

        Assert.Equal(["Questionable - Quest Battles"], _deleted);
    }

    [Fact]
    public void Remove_WaitsForCleanup()
    {
        Add(BossModIpc.EPreset.Overworld);
        Type session = typeof(BossModIpc).GetNestedType("Session", BindingFlags.NonPublic)!;
        Set("_session", session.GetConstructors()[0].Invoke([false]));

        _ipc.RemovePresets();
        Assert.Empty(_deleted);

        Set("_session", null);
        typeof(BossModIpc).GetMethod("TryRemovePresets", Flags)!.Invoke(_ipc, null);
        Assert.Equal(["Questionable"], _deleted);
    }

    [Fact]
    public void Restart_CancelsRemoval()
    {
        Add(BossModIpc.EPreset.Overworld);
        Type session = typeof(BossModIpc).GetNestedType("Session", BindingFlags.NonPublic)!;
        Set("_session", session.GetConstructors()[0].Invoke([false]));
        _ipc.RemovePresets();

        _ipc.KeepPresets();
        Set("_session", null);
        typeof(BossModIpc).GetMethod("TryRemovePresets", Flags)!.Invoke(_ipc, null);

        Assert.Empty(_deleted);
    }

    [Fact]
    public void RemovedPreset_IsRewrittenOnNextUse()
    {
        Add(BossModIpc.EPreset.Overworld);
        _ipc.RemovePresets();

        Add(BossModIpc.EPreset.Overworld);

        Assert.Equal(["Questionable", "Questionable"], _created);
        Assert.True(_presets.ContainsKey("Questionable"));
    }

    private void Add(BossModIpc.EPreset preset) =>
        typeof(BossModIpc).GetMethod("AddPreset", Flags)!.Invoke(_ipc, [preset]);

    private void Set(string field, object? value) =>
        typeof(BossModIpc).GetField(field, Flags)!.SetValue(_ipc, value);
}
