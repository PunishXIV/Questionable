namespace Questionable.Controller.CombatModules;

internal interface ICombatModule
{
    string Name { get; }

    bool CanHandleFight(CombatController.CombatData combatData);

    bool Start(CombatController.CombatData combatData);

    bool Stop();

    bool Pause();

    bool Resume();

    void VerifyActive() { }

    bool MovesPlayer => false;

    void Update(IGameObject nextTarget);

    bool CanAttack(IBattleNpc target);
}
