using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class StylistIpc : Ipc, IDisposable
{
    private const string PluginName = "Stylist";

    public override string InternalName => PluginName;
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);
    private readonly ILogger<StylistIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    // moveItemsFromInventory, shouldEquip
    [EzIPC("UpdateCurrentGearsetEx")] private readonly Action<bool?, bool?> _updateGearset = null!;

    public StylistIpc(ILogger<StylistIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, PluginName, SafeWrapper.None);
    }

    private bool _loggedIpcError;

    public static bool IsInstalled => IPCSubscriber.IsInstalled(PluginName);

    public bool WarnedMissing { get; set; }

    public bool? IsBusy => IsInstalled ? IpcInvoke.SafeFunc<bool?>(() => _isBusy(), fallback: null) : null;

    public bool UpdateGearset()
    {
        try
        {
            _updateGearset(true, true);
            return true;
        }
        catch (Exception e) when (IpcInvoke.IsSafeFailure(e))
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                _logger.LogWarning(e, "Could not query stylist to update gearset, probably not installed");
            }

            return false;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
