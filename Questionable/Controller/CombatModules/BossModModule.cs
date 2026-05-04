using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
using Questionable.External;
namespace Questionable.Controller.CombatModules;

internal sealed class BossModModule
(
    ILogger<BossModModule> logger,
    BossModIpc bossModIpc,
    Configuration configuration) : ICombatModule, IDisposable
{
    private readonly BossModIpc _bossModIpc = bossModIpc;
    private readonly Configuration _configuration = configuration;
    private readonly ILogger<BossModModule> _logger = logger;

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (_configuration.General.CombatModule != Configuration.ECombatModule.BossMod)
            return false;

        return _bossModIpc.IsSupported();
    }

    public bool Start(CombatController.CombatData combatData)
    {
        try
        {
            _bossModIpc.SetPreset(BossModIpc.EPreset.Overworld);
            return true;
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not start combat");
            return false;
        }
    }

    public bool Stop()
    {
        try
        {
            _bossModIpc.ClearPreset();
            return true;
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not turn off combat");
            return false;
        }
    }

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target)
    {
        return true;
    }

    public void Dispose()
    {
        Stop();
    }
}
