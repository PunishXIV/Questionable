using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutoDutyIpc : Ipc, IDisposable
{
    public delegate void RunForPluginDelegate(string pluginName, uint territoryType, int loops = 0, bool bareMode = false);

    [EzIPC] public Func<uint, bool> ContentHasPath { get; private set; } = null!;
    [EzIPC] public Func<bool> IsStopped { get; private set; } = null!;
    [EzIPC] public Action Stop { get; private set; } = null!;
    [EzIPC] public Func<string, string> GetConfig { get; private set; } = null!;
    [EzIPC] public Action<string, object> SetConfig { get; private set; } = null!;
    [EzIPC] public Action<string, bool> PluginConfig { get; private set; } = null!;
    [EzIPC] public RunForPluginDelegate RunForPlugin { get; private set; } = null!;

    private readonly EzIPCDisposalToken[] _disposalTokens = [];

    public AutoDutyIpc() => _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);

    // Shared fixtures only need to report whether AutoDuty is busy.
    internal AutoDutyIpc(Func<bool> isStopped) => IsStopped = isStopped;

    public override string InternalName => "AutoDuty";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => GetStopped() != null;

    public bool IsAvailable()
    {
        try
        {
            IsStopped();
            return true;
        }
        catch (Exception e) when (e is IpcNotReadyError || e.InnerException is IpcNotReadyError)
        {
            return false;
        }
        catch (Exception)
        {
            // A failed status read does not prove absence; protect possible manual work.
            return true;
        }
    }

    public bool? GetStopped()
    {
        try { return IsStopped(); }
        catch (Exception) { return null; }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
