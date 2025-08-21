using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using Questionable.External;

namespace Questionable.Controller.CombatModules;

internal sealed class BossModModule : ICombatModule, IDisposable
{
    private readonly ILogger<BossModModule> _logger;
    private readonly BossModIpc _bossModIpc;
    private readonly Configuration _configuration;

    public BossModModule(
        ILogger<BossModModule> logger,
        BossModIpc bossModIpc,
        Configuration configuration)
    {
        _logger = logger;
        _bossModIpc = bossModIpc;
        _configuration = configuration;
    }

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

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => Stop();
}
