using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class MogmailIpc(IDalamudPluginInterface pluginInterface, ILogger<MogmailIpc> logger)
{
    private readonly ICallGateSubscriber<bool> _isAvailable = pluginInterface.GetIpcSubscriber<bool>("Mogmail.IsAvailable");
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Mogmail.IsBusy");
    private readonly ICallGateSubscriber<bool> _claimAll = pluginInterface.GetIpcSubscriber<bool>("Mogmail.ClaimAll");
    private bool _loggedIpcError;

    public bool IsInstalled => IPCSubscriber.IsInstalled("Mogmail") && _isAvailable.InvokeFunc();

    public bool IsBusy => !IsInstalled || _isBusy.InvokeFunc();

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
            return _claimAll.InvokeFunc();
        }
        catch (IpcError e)
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                logger.LogWarning(e, "Could not call Mogmail.ClaimAll");
            }
            return false;
        }
    }
}