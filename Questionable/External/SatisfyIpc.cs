using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class SatisfyIpc : Ipc, IDisposable
{
    private readonly ILogger<SatisfyIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("Start")] private readonly Func<uint, int, bool> _start = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("Stop")] private readonly Action _stop = null!;

    public SatisfyIpc(ILogger<SatisfyIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "vsatisfy";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);

    public bool IsInstalled => IPCSubscriber.IsInstalled(InternalName);

    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy(), fallback: false);

    // False when Satisfy refused: busy, NPC locked or no deliveries left this week.
    public bool Start(uint npc, Configuration.EDeliveryItem item) =>
        IpcInvoke.SafeFunc(() => _start(npc, (int)item), fallback: false, _logger, "Could not call vsatisfy.Start");

    public void Stop() =>
        IpcInvoke.SafeFunc(() => { _stop(); return true; }, fallback: false, _logger, "Could not call vsatisfy.Stop");

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
