using Dalamud.Game.ClientState.Objects.Types;
using Questionable.Model.Questing;
namespace Questionable.Controller.CombatModules;

/// <summary>
///     Commandeered Magitek Armor; used in 'Magiteknical Failure' quest.
/// </summary>
[RegisterSingleton<ICombatModule, Mount128Module>(Duplicate = DuplicateStrategy.Append)]
internal sealed class Mount128Module(GameFunctions gameFunctions) : ICombatModule
{
    public const ushort MountId = 128;
    private readonly EAction[] _actions = [EAction.MagitekThunder, EAction.MagitekPulse];

    public bool CanHandleFight(CombatController.CombatData combatData) => GameFunctions.GetMountId() == MountId;

    public bool Start(CombatController.CombatData combatData) => true;

    public bool Stop() => true;

    public void Update(IGameObject gameObject)
    {
        foreach (EAction action in _actions)
        {
            if (gameFunctions.UseAction(gameObject, action, checkCanUse: false))
                return;
        }
    }

    public bool CanAttack(IBattleNpc target) => GameFunctions.GetBaseID(target) is 7504 or 7505 or 14107;
}
