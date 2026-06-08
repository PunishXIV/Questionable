using Dalamud.Game.ClientState.Objects.Types;
using Questionable.Functions;
using Questionable.Model.Questing;
namespace Questionable.Controller.CombatModules;

/// <summary>
///     Commandeered Magitek Armor; used in 'Magiteknical Failure' quest.
/// </summary>
internal sealed class Mount147Module(GameFunctions gameFunctions) : ICombatModule
{
    public const ushort MountId = 147;
    private readonly EAction[] _actions = [EAction.Trample];

    public bool CanHandleFight(CombatController.CombatData combatData) => GameFunctions.GetMountId() == MountId;

    public bool Start(CombatController.CombatData combatData) => true;

    public bool Stop() => true;

    public void Update(IGameObject gameObject)
    {
        foreach (EAction action in _actions)
        {
            if (gameFunctions.UseAction(gameObject, action, false))
                return;
        }
    }

    public bool CanAttack(IBattleNpc target) => GameFunctions.GetBaseID(target) is 8593;
}
