using Dalamud.Game.ClientState.Objects.Types;
using Questionable.Functions;
using Questionable.Model.Questing;

namespace Questionable.Controller.CombatModules;

/// <summary>
/// Commandeered Magitek Armor; used in 'Magiteknical Failure' quest.
/// </summary>
internal sealed class Mount128Module : ICombatModule
{
    public const ushort MountId = 128;
    private readonly EAction[] _actions = [EAction.MagitekThunder, EAction.MagitekPulse];

    private readonly GameFunctions _gameFunctions;

    public Mount128Module(GameFunctions gameFunctions)
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

    public bool CanAttack(IBattleNpc target) => target.DataId is 7504 or 7505 or 14107;
}
