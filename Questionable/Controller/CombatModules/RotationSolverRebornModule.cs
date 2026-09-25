using Dalamud.Game.ClientState.Objects.Types;
using JetBrains.Annotations;
namespace Questionable.Controller.CombatModules;

[RegisterSingleton<ICombatModule, RotationSolverRebornModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class RotationSolverRebornModule(
    RotationSolverRebornIpc rotationSolverRebornIpc,
    Mount128Module mount128Module,
    Mount147Module mount147Module) : ICombatModule, IDisposable
{
    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (mount128Module.CanHandleFight(combatData) ||
            mount147Module.CanHandleFight(combatData))
            return false;

        return rotationSolverRebornIpc.IsEnabled;
    }

    public bool Start(CombatController.CombatData combatData) => rotationSolverRebornIpc.RotationAuto();

    public bool Stop()
    {
        rotationSolverRebornIpc.RotationStop();
        return true;
    }

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => Stop();

    [PublicAPI]
    private enum StateCommandType : byte
    {
        Off,
        Auto,
        TargetOnly,
        Manual
    }
}
