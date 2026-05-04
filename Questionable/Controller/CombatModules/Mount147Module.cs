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

    private readonly GameFunctions _gameFunctions = gameFunctions;

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        return _gameFunctions.GetMountId() == MountId;
    }

    public bool Start(CombatController.CombatData combatData)
    {
        return true;
    }

    public bool Stop()
    {
        return true;
    }

    public void Update(IGameObject gameObject)
    {
        foreach (EAction action in _actions)
        {
            if (_gameFunctions.UseAction(gameObject, action, false))
                return;
        }
    }

    public bool CanAttack(IBattleNpc target)
    {
        return GameFunctions.GetBaseID(target) is 8593;
    }
}
