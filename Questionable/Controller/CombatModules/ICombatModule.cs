using Dalamud.Game.ClientState.Objects.Types;
namespace Questionable.Controller.CombatModules;

internal interface ICombatModule
{
    bool CanHandleFight(CombatController.CombatData combatData);

    bool Start(CombatController.CombatData combatData);

    bool Stop();

    // Called even while moving or waiting for a target. Local combat modules need no ownership check.
    void VerifyActive() { }

    void Update(IGameObject nextTarget);

    bool CanAttack(IBattleNpc target);
}
