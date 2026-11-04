using Dalamud.Game.ClientState.Objects.Types;
namespace Questionable.Controller.CombatModules;

[RegisterSingleton<ICombatModule, RotationSolverRebornModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class RotationSolverRebornModule(RotationSolverRebornIpc rotationSolverRebornIpc) : ICombatModule, IDisposable
{
    public bool CanHandleFight(CombatController.CombatData combatData) => rotationSolverRebornIpc.IsEnabled;

    public bool Start(CombatController.CombatData combatData) => rotationSolverRebornIpc.RotationAuto();

    public bool Stop() => rotationSolverRebornIpc.RotationStop();

    public bool Pause() => rotationSolverRebornIpc.RotationPause();

    public bool Resume() => rotationSolverRebornIpc.RotationResume();

    public void VerifyActive() => rotationSolverRebornIpc.VerifyActive();

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => Stop();
}
