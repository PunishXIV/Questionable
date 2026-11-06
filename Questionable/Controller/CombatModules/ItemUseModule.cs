using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.CombatModules;

[RegisterSingleton<ICombatModule, ItemUseModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class ItemUseModule(IServiceProvider serviceProvider, ICondition condition, MovementController movementController, ILogger<ItemUseModule> logger) : ICombatModule
{
    private CombatController.CombatData? _combatData;
    private DateTime _continueAt;

    private ICombatModule? _delegate;
    private bool _isDoingRotation;

    public string Name => _delegate?.Name ?? "Item use";

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (combatData.CombatItemUse == null)
            return false;

        _delegate = serviceProvider.GetRequiredService<IEnumerable<ICombatModule>>()
            .FirstOrDefault(x => x is not ItemUseModule && x.CanHandleFight(combatData));
        logger.LogInformation("ItemUse delegate: {Delegate}", _delegate?.GetType().Name);
        return _delegate != null;
    }

    public bool Start(CombatController.CombatData combatData)
    {
        _combatData = combatData;
        if (_delegate == null || !_delegate.Start(combatData))
            throw new TaskException("Unable to start rotation for combat item use.");
        _isDoingRotation = true;
        _continueAt = DateTime.Now;
        return true;
    }

    public bool Stop()
    {
        // A failed start or an item-use pause may still own partial provider state.
        if (_delegate != null && !_delegate.Stop())
            return false;
        _isDoingRotation = false;
        _combatData = null;
        _delegate = null;
        _continueAt = DateTime.Now;
        return true;
    }

    public void VerifyActive()
    {
        if (_combatData != null)
            _delegate!.VerifyActive();
    }

    internal void StartRotation()
    {
        if (_delegate == null || _combatData == null || !_delegate.Resume())
            throw new TaskException("Unable to start rotation for combat item use.");
        _isDoingRotation = true;
    }

    internal void StopRotation()
    {
        if (_delegate == null || !_delegate.Pause())
            throw new TaskException("Waiting for rotation to stop before using a combat item.");
        _isDoingRotation = false;
    }

    public bool Pause()
    {
        StopRotation();
        return true;
    }

    public bool Resume()
    {
        StartRotation();
        return true;
    }

    public void Update(IGameObject nextTarget)
    {
        if (_delegate == null)
            return;

        if (_continueAt > DateTime.Now)
            return;

        if (_combatData?.CombatItemUse == null)
        {
            _delegate.Update(nextTarget);
            return;
        }

        if (_combatData.KillEnemyDataIds.Contains(GameFunctions.GetBaseID(nextTarget)) ||
            _combatData.ComplexCombatDatas.Any(x => x.DataId == GameFunctions.GetBaseID(nextTarget) &&
                                                    (x.NameId == null || (nextTarget is ICharacter character && x.NameId == character.NameId))))
        {
            if (nextTarget.Position.DistanceTo_XZ(Svc.Objects[0]!.Position) > 3f)
            {
                logger.LogInformation("Too far from target, moving closer");
                movementController.NavigateTo(EMovementType.Combat, nextTarget.BaseId, nextTarget.Position, new() { StopDistance = 3f });
                _continueAt = DateTime.Now.AddSeconds(1);
                return;
            }
            if (_isDoingRotation)
            {
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetInventoryItemCount(_combatData.CombatItemUse.ItemId) == 0)
                    {
                        StopRotation();
                        return;
                    }
                }

                if (ShouldUseItem(nextTarget))
                {
                    StopRotation();
                    unsafe
                    {
                        logger.LogInformation("Using item {ItemId}", _combatData.CombatItemUse.ItemId);
                        AgentInventoryContext.Instance()->UseItem(_combatData.CombatItemUse.ItemId);
                    }

                    _continueAt = DateTime.Now.AddSeconds(2);
                }
                else
                    _delegate.Update(nextTarget);
            }
            else if (condition[ConditionFlag.Casting])
            {
                DateTime alternativeContinueAt = DateTime.Now.AddSeconds(0.5);
                if (alternativeContinueAt > _continueAt)
                    _continueAt = alternativeContinueAt;
            }
            else
            {
                StartRotation();
            }
        }
        else if (_isDoingRotation)
            _delegate.Update(nextTarget);
    }

    public bool CanAttack(IBattleNpc target) => _delegate!.CanAttack(target);

    private unsafe bool ShouldUseItem(IGameObject gameObject)
    {
        if (_combatData?.CombatItemUse == null)
            return false;

        if (gameObject is IBattleChara)
        {
            BattleChara* battleChara = (BattleChara*)gameObject.Address;
            if (_combatData.CombatItemUse.Condition == ECombatItemUseCondition.Incapacitated)
                return (battleChara->ActorControlFlags & 0x40) != 0;

            if (_combatData.CombatItemUse.Condition == ECombatItemUseCondition.HealthPercent)
                return (100f * battleChara->Health / battleChara->MaxHealth) < _combatData.CombatItemUse.Value;

            if (_combatData.CombatItemUse.Condition == ECombatItemUseCondition.MissingStatus)
                return !battleChara->StatusManager.HasStatus((uint)_combatData.CombatItemUse.Value);
        }

        return false;
    }
}
