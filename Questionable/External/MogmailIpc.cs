using FFXIVClientStructs.FFXIV.Client.UI.Info;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class MogmailIpc : Ipc, IDisposable
{
    private readonly ILogger<MogmailIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("IsAvailable")] private readonly Func<bool> _isAvailable = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("ClaimAll")] private readonly Func<bool> _claimAll = null!;

    public MogmailIpc(ILogger<MogmailIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Mogmail";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);
    private bool _loggedIpcError;

    public bool IsInstalled => IPCSubscriber.IsInstalled("Mogmail") && _isAvailable();

    public bool IsBusy => !IsInstalled || _isBusy();

    public unsafe static uint? LetterCount
    {
        get
        {
            InfoProxyLetter* proxyLetter = InfoProxyLetter.Instance();
            if (proxyLetter != null)
                return proxyLetter->NumLettersFromPurchases;
            return null;
        }
    }

    public bool ClaimAll()
    {
        try
        {
            return _claimAll();
        }
        catch (Exception e) when (IpcInvoke.IsSafeIpcFailure(e))
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                _logger.LogWarning(e, "Could not call Mogmail.ClaimAll");
            }
            return false;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
