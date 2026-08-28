using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutomatonIpc
{
    private const string AutoSnipeTweak = "AutoSnipeQuests";

    private readonly ICallGateSubscriber<string, bool> _isTweakEnabled;
    private readonly ICallGateSubscriber<string, bool, object> _setTweakState;
    private readonly ILogger<AutomatonIpc> _logger;
    private bool _loggedIpcError;

    public AutomatonIpc(IDalamudPluginInterface pluginInterface, ILogger<AutomatonIpc> logger)
    {
        _logger = logger;
        _isTweakEnabled = pluginInterface.GetIpcSubscriber<string, bool>("Automaton.IsTweakEnabled");
        _setTweakState = pluginInterface.GetIpcSubscriber<string, bool, object>("Automaton.SetTweakState");
        logger.LogInformation("Automaton auto-snipe enabled: {IsTweakEnabled}", IsAutoSnipeEnabled);
    }

    public bool IsAutoSnipeEnabled
    {
        get
        {
            try
            {
                return _isTweakEnabled.InvokeFunc(AutoSnipeTweak);
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
            catch (IpcError e)
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
            _setTweakState.InvokeAction(AutoSnipeTweak, enabled);
            return IsAutoSnipeEnabled == enabled;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not set automaton auto-snipe to {Enabled}", enabled);
            return false;
        }
    }
}
