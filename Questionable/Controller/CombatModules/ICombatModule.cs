using Dalamud.Game.ClientState.Objects.Types;

namespace Questionable.Controller.CombatModules;

internal interface ICombatModule
{
    bool CanHandleFight(CombatController.CombatData combatData);

    bool Start(CombatController.CombatData combatData);

    bool Stop();

    void Update(IGameObject nextTarget);

    bool CanAttack(IBattleNpc target);
}
