using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using Questionable.Model.Common;
namespace Questionable.Controller.CombatModules;

[RegisterSingleton<ICombatModule, BossModModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class BossModModule
(
    ILogger<BossModModule> logger,
    BossModIpc bossModIpc,
    Configuration configuration,
    Mount128Module mount128Module,
    Mount147Module mount147Module,
    IFramework framework) : ICombatModule, IDisposable
{
    private bool _justLoaded = true;

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (configuration.General.CombatModule != ECombatModule.BossMod)
            return false;

        if (mount128Module.CanHandleFight(combatData) ||
            mount147Module.CanHandleFight(combatData))
            return false;

        return bossModIpc.IsSupported();
    }

    public bool Start(CombatController.CombatData combatData)
    {
        try
        {
            if (_justLoaded)
            {
                bossModIpc.AddAllPresets(delete: true);
                _justLoaded = false;
            }
            bossModIpc.SetActivePreset(BossModIpc.EPreset.Overworld);
            return true;
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Could not start combat");
            return false;
        }
    }

    public bool Stop()
    {
        try
        {
            bossModIpc.Cleanup();
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not turn off combat");
            return false;
        }
    }

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(framework, () => Stop(), logger);
}
