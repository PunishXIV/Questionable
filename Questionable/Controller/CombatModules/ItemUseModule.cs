using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;

namespace Questionable.Controller.CombatModules;

internal sealed class ItemUseModule : ICombatModule
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICondition _condition;
    private readonly ILogger<ItemUseModule> _logger;

    private ICombatModule? _delegate;
    private CombatController.CombatData? _combatData;
    private bool _isDoingRotation;
    private DateTime _continueAt;

    public ItemUseModule(IServiceProvider serviceProvider, ICondition condition, ILogger<ItemUseModule> logger)
    {
        _serviceProvider = serviceProvider;
        _condition = condition;
        _logger = logger;
    }

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (combatData.CombatItemUse == null)
            return false;

        _delegate = _serviceProvider.GetRequiredService<IEnumerable<ICombatModule>>()
            .Where(x => x is not ItemUseModule)
            .FirstOrDefault(x => x.CanHandleFight(combatData));
        _logger.LogInformation("ItemUse delegate: {Delegate}", _delegate?.GetType().Name);
        return _delegate != null;
    }

    public bool Start(CombatController.CombatData combatData)
    {
        if (_delegate!.Start(combatData))
        {
            _combatData = combatData;
            _isDoingRotation = true;
            _continueAt = DateTime.Now;
            return true;
        }

        return false;
    }

    public bool Stop()
    {
        if (_isDoingRotation)
        {
            _delegate!.Stop();
            _isDoingRotation = false;
            _combatData = null;
            _delegate = null;
            _continueAt = DateTime.Now;
        }

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

        if (_combatData.KillEnemyDataIds.Contains(nextTarget.DataId) ||
            _combatData.ComplexCombatDatas.Any(x => x.DataId == nextTarget.DataId &&
                                                    (x.NameId == null || (nextTarget is ICharacter character && x.NameId == character.NameId))))
        {
            if (_isDoingRotation)
            {
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetInventoryItemCount(_combatData.CombatItemUse.ItemId) == 0)
                    {
                        _isDoingRotation = false;
                        _delegate.Stop();
                        return;
                    }
                }

                if (ShouldUseItem(nextTarget))
                {
                    _isDoingRotation = false;
                    _delegate.Stop();
                    unsafe
                    {
                        _logger.LogInformation("Using item {ItemId}", _combatData.CombatItemUse.ItemId);
                        AgentInventoryContext.Instance()->UseItem(_combatData.CombatItemUse.ItemId);
                    }
                    _continueAt = DateTime.Now.AddSeconds(2);
                }
                else
                    _delegate.Update(nextTarget);
            }
            else if (_condition[ConditionFlag.Casting])
            {
                // do nothing
                DateTime alternativeContinueAt = DateTime.Now.AddSeconds(0.5);
                if (alternativeContinueAt > _continueAt)
                    _continueAt = alternativeContinueAt;
            }
            else
            {
                _isDoingRotation = true;
                _delegate.Start(_combatData);
            }
        }
        else if (_isDoingRotation)
        {
            _delegate.Update(nextTarget);
        }
    }

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

    public bool CanAttack(IBattleNpc target) => _delegate!.CanAttack(target);
}
