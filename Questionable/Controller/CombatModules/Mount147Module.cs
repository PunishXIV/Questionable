using Dalamud.Game.ClientState.Objects.Types;
using Questionable.Functions;
using Questionable.Model.Questing;

namespace Questionable.Controller.CombatModules;

/// <summary>
/// Commandeered Magitek Armor; used in 'Magiteknical Failure' quest.
/// </summary>
internal sealed class Mount147Module : ICombatModule
{
    public const ushort MountId = 147;
    private readonly EAction[] _actions = [EAction.Trample];

    private readonly GameFunctions _gameFunctions;


    public Mount147Module(GameFunctions gameFunctions)
    {
        _gameFunctions = gameFunctions;
    }

    public bool CanHandleFight(CombatController.CombatData combatData) => _gameFunctions.GetMountId() == MountId;

    public bool Start(CombatController.CombatData combatData) => true;

    public bool Stop() => true;

    public void Update(IGameObject gameObject)
    {
        foreach (EAction action in _actions)
        {
            if (_gameFunctions.UseAction(gameObject, action, checkCanUse: false))
                return;
        }
    }

    public bool CanAttack(IBattleNpc target) => target.DataId is 8593;
}
