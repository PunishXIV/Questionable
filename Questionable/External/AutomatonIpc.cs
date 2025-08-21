using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;

namespace Questionable.External;

internal sealed class AutomatonIpc
{
    private readonly ILogger<AutomatonIpc> _logger;
    private readonly ICallGateSubscriber<string,bool> _isTweakEnabled;
    private bool _loggedIpcError;

    public AutomatonIpc(IDalamudPluginInterface pluginInterface, ILogger<AutomatonIpc> logger)
    {
        _logger = logger;
        _isTweakEnabled = pluginInterface.GetIpcSubscriber<string, bool>("Automaton.IsTweakEnabled");
        logger.LogInformation("Automaton auto-snipe enabled: {IsTweakEnabled}", IsAutoSnipeEnabled);
    }

    public bool IsAutoSnipeEnabled
    {
        get
        {
            try
            {
                return _isTweakEnabled.InvokeFunc("AutoSnipeQuests");
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
}
