using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class NavmeshIpc : Ipc, IDisposable
{
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("Nav.IsReady")] private readonly Func<bool> _isNavReady = null!;
    [EzIPC("Path.IsRunning")] private readonly Func<bool> _pathIsRunning = null!;
    [EzIPC("SimpleMove.PathfindInProgress")] private readonly Func<bool> _simpleMoveInProgress = null!;
    [EzIPC("Nav.PathfindInProgress")] private readonly Func<bool> _pathfindInProgress = null!;
    [EzIPC("SimpleMove.PathfindAndMoveTo")] private readonly Func<Vector3, bool, bool> _pathfindAndMoveTo = null!;
    [EzIPC("Path.Stop")] private readonly Action _pathStop = null!;

    public NavmeshIpc()
    {
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "vnavmesh";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && _isNavReady(), fallback: false);

    public bool? GetPathRunning() => ReadOptional(() => _pathIsRunning());
    public bool? GetSimpleMoveInProgress() => ReadOptional(() => _simpleMoveInProgress());
    public bool? GetPathfindInProgress() => ReadOptional(() => _pathfindInProgress());
    public bool WalkTo(Vector3 destination) => IpcInvoke.SafeFunc(() => _pathfindAndMoveTo(destination, false), fallback: false);
    public void Stop() => IpcInvoke.SafeAction(() => _pathStop());

    private static bool? ReadOptional(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
