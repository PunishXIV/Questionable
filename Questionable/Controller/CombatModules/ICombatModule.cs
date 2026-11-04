using Dalamud.Game.ClientState.Objects.Types;
namespace Questionable.Controller.CombatModules;

internal interface ICombatModule
{
    bool CanHandleFight(CombatController.CombatData combatData);

    bool Start(CombatController.CombatData combatData);

    bool Stop();

    // Temporary item-use pauses retain ownership and the user's original provider state.
    bool Pause();

    bool Resume();

    // Called even while moving or waiting for a target. Local combat modules need no ownership check.
    void VerifyActive() { }

    void Update(IGameObject nextTarget);

    bool CanAttack(IBattleNpc target);
}
