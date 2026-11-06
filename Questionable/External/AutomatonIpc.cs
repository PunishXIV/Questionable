using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutomatonIpc : Ipc, IDisposable
{
    public override string InternalName => "Automaton";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = _isTweakEnabled(AutoSnipeTweak);
            return GetVersion() != null;
        }, fallback: false);
    private const string AutoSnipeTweak = "AutoSnipeQuests";

    [EzIPC("IsTweakEnabled")] private readonly Func<string, bool> _isTweakEnabled = null!;
    [EzIPC("SetTweakState")] private readonly Action<string, bool> _setTweakState = null!;
    private readonly EzIPCDisposalToken[] _disposalTokens;
    private readonly ILogger<AutomatonIpc> _logger;
    private bool _loggedIpcError;

    public AutomatonIpc(ILogger<AutomatonIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
        logger.LogInformation("Automaton auto-snipe enabled: {IsTweakEnabled}", IsAutoSnipeEnabled);
    }

    public bool IsAutoSnipeEnabled
    {
        get
        {
            try
            {
                return _isTweakEnabled(AutoSnipeTweak);
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
            catch (Exception e) when (IpcInvoke.IsSafeIpcFailure(e))
            {
                if (!_loggedIpcError)
                {
                    _loggedIpcError = true;
                    _logger.LogWarning(e, "Could not query automaton for tweak status, probably not installed");
                }

                return false;
            }
        }
    }

    public bool SetAutoSnipeEnabled(bool enabled)
    {
        try
        {
            _setTweakState(AutoSnipeTweak, enabled);
            return IsAutoSnipeEnabled == enabled;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception e) when (IpcInvoke.IsSafeIpcFailure(e))
        {
            _logger.LogWarning(e, "Could not set automaton auto-snipe to {Enabled}", enabled);
            return false;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
