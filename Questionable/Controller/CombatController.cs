using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Microsoft.Extensions.Logging;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.Controller.Utils;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;

namespace Questionable.Controller;

internal sealed class CombatController : IDisposable
{
    private const float MaxTargetRange = 55f;
    private const float MaxNameplateRange = 50f;

    private readonly List<ICombatModule> _combatModules;
    private readonly MovementController _movementController;
    private readonly ITargetManager _targetManager;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly QuestFunctions _questFunctions;
    private readonly ILogger<CombatController> _logger;

    private CurrentFight? _currentFight;
    private bool _wasInCombat;
    private ulong? _lastTargetId;
    private List<byte>? _previousQuestVariables;

    public CombatController(
        IEnumerable<ICombatModule> combatModules,
        MovementController movementController,
        ITargetManager targetManager,
        IObjectTable objectTable,
        ICondition condition,
        IClientState clientState,
        QuestFunctions questFunctions,
        ILogger<CombatController> logger)
    {
        _combatModules = combatModules.ToList();
        _movementController = movementController;
        _targetManager = targetManager;
        _objectTable = objectTable;
        _condition = condition;
        _clientState = clientState;
        _questFunctions = questFunctions;
        _logger = logger;

        _clientState.TerritoryChanged += TerritoryChanged;
    }

    public bool IsRunning => _currentFight != null;

    public bool Start(CombatData combatData)
    {
        Stop("Starting combat");

        var combatModule = _combatModules.FirstOrDefault(x => x.CanHandleFight(combatData));
        if (combatModule == null)
            return false;

        if (combatModule.Start(combatData))
        {
            _currentFight = new CurrentFight
            {
                Module = combatModule,
                Data = combatData,
                LastDistanceCheck = DateTime.Now,
            };
            _wasInCombat =
                combatData.SpawnType is EEnemySpawnType.QuestInterruption or EEnemySpawnType.FinishCombatIfAny;
            UpdateLastTargetAndQuestVariables(null);
            return true;
        }
        else
            return false;
    }

    public EStatus Update()
    {
        if (_currentFight == null)
            return EStatus.Complete;

        if (_movementController.IsPathfinding ||
            _movementController.IsPathRunning ||
            _movementController.MovementStartedAt > DateTime.Now.AddSeconds(-1))
            return EStatus.Moving;

        // Overworld enemies typically means that if we want to kill 3 enemies, we could have anywhere from 0 to 20
        // enemies in the area (0 if someone else killed them before, like can happen with bots in Fools' Falls in
        // La Noscea).
        //
        // For all 'normal' types, e.g. auto-spawning on entering an area, there's a fixed number of enemies that you're
        // fighting with, and the enemies in the overworld aren't relevant.
        if (_currentFight.Data.SpawnType is EEnemySpawnType.OverworldEnemies)
        {
            if (_targetManager.Target != null)
                _lastTargetId = _targetManager.Target?.GameObjectId;
            else
            {
                if (_lastTargetId != null)
                {
                    IGameObject? lastTarget = _objectTable.FirstOrDefault(x => x.GameObjectId == _lastTargetId);
                    if (lastTarget != null)
                    {
                        // wait until the game cleans up the target
                        if (lastTarget.IsDead)
                        {
                            ElementId? elementId = _currentFight.Data.ElementId;
                            QuestProgressInfo? questProgressInfo = elementId != null
                                ? _questFunctions.GetQuestProgressInfo(elementId)
                                : null;

                            if (questProgressInfo != null &&
                                questProgressInfo.Sequence == _currentFight.Data.Sequence &&
                                QuestWorkUtils.HasCompletionFlags(_currentFight.Data.CompletionQuestVariablesFlags) &&
                                QuestWorkUtils.MatchesQuestWork(_currentFight.Data.CompletionQuestVariablesFlags,
                                    questProgressInfo))
                            {
                                // would be the final enemy of the bunch
                                return EStatus.InCombat;
                            }
                            else if (questProgressInfo != null &&
                                     questProgressInfo.Sequence == _currentFight.Data.Sequence &&
                                     _previousQuestVariables != null &&
                                     !questProgressInfo.Variables.SequenceEqual(_previousQuestVariables))
                            {
                                UpdateLastTargetAndQuestVariables(null);
                            }
                            else
                                return EStatus.InCombat;
                        }
                    }
                    else
                        _lastTargetId = null;
                }
            }
        }

        var target = _targetManager.Target;
        if (target != null)
        {
            int currentTargetPriority = GetKillPriority(target).Priority;
            var nextTarget = FindNextTarget();
            int nextTargetPriority = nextTarget != null ? GetKillPriority(nextTarget).Priority : 0;

            if (nextTarget != null && nextTarget.Equals(target))
            {
                if (!IsMovingOrShouldMove(target))
                {
                    try
                    {
                        _currentFight.Module.Update(target);
                    }
                    catch (TaskException e)
                    {
                        _logger.LogWarning(e, "Combat was interrupted, stopping: {Exception}", e.Message);
                        SetTarget(null);
                    }
                }
            }
            else if (nextTarget != null)
            {
                if (nextTargetPriority > currentTargetPriority || currentTargetPriority == 0)
                    SetTarget(nextTarget);
            }
            else
                SetTarget(null);
        }
        else
        {
            var nextTarget = FindNextTarget();
            if (nextTarget is { IsDead: false })
                SetTarget(nextTarget);
        }

        if (_condition[ConditionFlag.InCombat])
        {
            _wasInCombat = true;
            return EStatus.InCombat;
        }
        else if (_wasInCombat)
            return EStatus.Complete;
        else
            return EStatus.InCombat;
    }

    [SuppressMessage("ReSharper", "RedundantJumpStatement")]
    private IGameObject? FindNextTarget()
    {
        if (_currentFight == null)
            return null;

        // check if any complex combat conditions are fulfilled
        var complexCombatData = _currentFight.Data.ComplexCombatDatas;
        if (complexCombatData.Count > 0)
        {
            for (int i = 0; i < complexCombatData.Count; ++i)
            {
                if (_currentFight.Data.CompletedComplexDatas.Contains(i))
                    continue;

                var condition = complexCombatData[i];
                if (condition.RewardItemId != null && condition.RewardItemCount != null)
                {
                    unsafe
                    {
                        var inventoryManager = InventoryManager.Instance();
                        if (inventoryManager->GetInventoryItemCount(condition.RewardItemId.Value) >=
                            condition.RewardItemCount.Value)
                        {
                            _logger.LogInformation(
                                "Complex combat condition fulfilled: itemCount({ItemId}) >= {ItemCount}",
                                condition.RewardItemId, condition.RewardItemCount);
                            _currentFight.Data.CompletedComplexDatas.Add(i);
                            continue;
                        }
                    }
                }

                if (QuestWorkUtils.HasCompletionFlags(condition.CompletionQuestVariablesFlags) &&
                    _currentFight.Data.ElementId is QuestId questId)
                {
                    var questWork = _questFunctions.GetQuestProgressInfo(questId);
                    if (questWork != null &&
                        QuestWorkUtils.MatchesQuestWork(condition.CompletionQuestVariablesFlags, questWork))
                    {
                        _logger.LogInformation("Complex combat condition fulfilled: QuestWork matches");
                        _currentFight.Data.CompletedComplexDatas.Add(i);
                        continue;
                    }
                }
            }
        }

        return _objectTable.Select(x => new
            {
                GameObject = x,
                GetKillPriority(x).Priority,
                Distance = Vector3.Distance(x.Position, _clientState.LocalPlayer!.Position),
            })
            .Where(x => x.Priority > 0)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Distance)
            .Select(x => x.GameObject)
            .FirstOrDefault();
    }

    public unsafe (int Priority, string Reason) GetKillPriority(IGameObject gameObject)
    {
        (int? rawPriority, string reason) = GetRawKillPriority(gameObject);
        if (rawPriority == null)
            return (0, reason);

        // priority is a value between 0 and 100 inclusive; we want to always kill enemies we have fight with on first
        if (gameObject is IBattleNpc battleNpc && battleNpc.StatusFlags.HasFlag(StatusFlags.InCombat))
        {
            // stuff trying to kill us
            if (gameObject.TargetObjectId == _clientState.LocalPlayer?.GameObjectId)
                return (rawPriority.Value + 150, reason + "/Targeted");

            // stuff on our enmity list that's not necessarily targeting us
            var haters = UIState.Instance()->Hater;
            for (int i = 0; i < haters.HaterCount; ++i)
            {
                var hater = haters.Haters[i];
                if (hater.EntityId == gameObject.GameObjectId)
                    return (rawPriority.Value + 125, reason + "/Enmity");
            }
        }

        return (rawPriority.Value, reason);
    }

    private unsafe (int? Priority, string Reason) GetRawKillPriority(IGameObject gameObject)
    {
        if (_currentFight == null)
            return (null, "Not Fighting");

        if (gameObject is IBattleNpc battleNpc)
        {
            if (!_currentFight.Module.CanAttack(battleNpc))
                return (null, "Can't attack");

            if (battleNpc.IsDead)
                return (null, "Dead");

            if (!battleNpc.IsTargetable)
                return (null, "Untargetable");

            var complexCombatData = _currentFight.Data.ComplexCombatDatas;
            var gameObjectStruct = (GameObject*)gameObject.Address;
            if (gameObjectStruct->FateId != 0 && gameObject.TargetObjectId != _clientState.LocalPlayer?.GameObjectId)
                return (null, "FATE mob");

            var ownPosition = _clientState.LocalPlayer?.Position ?? Vector3.Zero;
            bool expectQuestMarker;
            if (_currentFight.Data.SpawnType == EEnemySpawnType.FinishCombatIfAny)
                expectQuestMarker = false;
            else if (_currentFight.Data.SpawnType == EEnemySpawnType.OverworldEnemies &&
                     Vector3.Distance(ownPosition, battleNpc.Position) >= MaxNameplateRange)
                expectQuestMarker = false;
            else
                expectQuestMarker = true;

            if (complexCombatData.Count > 0)
            {
                for (int i = 0; i < complexCombatData.Count; ++i)
                {
                    if (_currentFight.Data.CompletedComplexDatas.Contains(i))
                        continue;

                    if (expectQuestMarker &&
                        !complexCombatData[i].IgnoreQuestMarker &&
                        gameObjectStruct->NamePlateIconId == 0)
                        continue;

                    if (complexCombatData[i].DataId == battleNpc.DataId &&
                        (complexCombatData[i].NameId == null || complexCombatData[i].NameId == battleNpc.NameId))
                        return (100, "CCD");
                }
            }
            else
            {
                if ((!expectQuestMarker || gameObjectStruct->NamePlateIconId != 0) &&
                    _currentFight.Data.KillEnemyDataIds.Contains(battleNpc.DataId))
                    return (90, "KED");
            }

            // enemies that we have aggro on
            if (battleNpc.BattleNpcKind is BattleNpcSubKind.BattleNpcPart or BattleNpcSubKind.Enemy)
            {
                // npc that starts a fate or does turn-ins; not sure why they're marked as hostile
                if (gameObjectStruct->NamePlateIconId is 60093 or 60732)
                    return (null, "FATE NPC");

                return (0, "Not part of quest");
            }

            return (null, "Wrong BattleNpcKind");
        }
        else
            return (null, "Not BattleNpc");
    }

    private void SetTarget(IGameObject? target)
    {
        if (target == null)
        {
            if (_targetManager.Target != null)
            {
                _logger.LogInformation("Clearing target");
                _targetManager.Target = null;
            }
        }
        else if (Vector3.Distance(_clientState.LocalPlayer!.Position, target.Position) > MaxTargetRange)
        {
            _logger.LogInformation("Moving to target, distance: {Distance:N2}",
                Vector3.Distance(_clientState.LocalPlayer!.Position, target.Position));
            MoveToTarget(target);
        }
        else
        {
            _logger.LogInformation("Setting target to {TargetName} ({TargetId:X8})", target.Name.ToString(),
                target.GameObjectId);
            _targetManager.Target = target;
            MoveToTarget(target);
        }
    }

    private bool IsMovingOrShouldMove(IGameObject gameObject)
    {
        if (_movementController.IsPathfinding || _movementController.IsPathRunning)
            return true;

        if (DateTime.Now > _currentFight!.LastDistanceCheck.AddSeconds(10))
        {
            MoveToTarget(gameObject);
            _currentFight!.LastDistanceCheck = DateTime.Now;
            return true;
        }

        return false;
    }

    private void MoveToTarget(IGameObject gameObject)
    {
        var player = _clientState.LocalPlayer;
        if (player == null)
            return; // uh oh

        float hitboxOffset = player.HitboxRadius + gameObject.HitboxRadius;
        float actualDistance = Vector3.Distance(player.Position, gameObject.Position);
        float maxDistance = player.ClassJob.ValueNullable?.Role is 3 or 4 ? 20f : 2.9f;
        bool outOfRange = actualDistance - hitboxOffset >= maxDistance;
        bool isInLineOfSight = IsInLineOfSight(gameObject);
        if (outOfRange || !isInLineOfSight)
        {
            bool useNavmesh = actualDistance - hitboxOffset > 5f;
            if (!outOfRange && !isInLineOfSight)
            {
                maxDistance = Math.Min(maxDistance, actualDistance) / 2;
                useNavmesh = true;
            }

            if (!useNavmesh)
            {
                _logger.LogInformation("Moving to {TargetName} ({DataId}) to attack", gameObject.Name,
                    gameObject.DataId);
                _movementController.NavigateTo(EMovementType.Combat, null, [gameObject.Position], false, false,
                    maxDistance + hitboxOffset - 0.25f, verticalStopDistance: float.MaxValue);
            }
            else
            {
                _logger.LogInformation("Moving to {TargetName} ({DataId}) to attack (with navmesh)", gameObject.Name,
                    gameObject.DataId);
                _movementController.NavigateTo(EMovementType.Combat, null, gameObject.Position, false, false,
                    maxDistance + hitboxOffset - 0.25f, verticalStopDistance: float.MaxValue);
            }
        }
    }

    internal unsafe bool IsInLineOfSight(IGameObject target)
    {
        Vector3 sourcePos = _clientState.LocalPlayer!.Position;
        sourcePos.Y += 2;

        Vector3 targetPos = target.Position;
        targetPos.Y += 2;

        Vector3 direction = targetPos - sourcePos;
        float distance = direction.Length();

        direction = Vector3.Normalize(direction);

        Vector3 originVect = new Vector3(sourcePos.X, sourcePos.Y, sourcePos.Z);
        Vector3 directionVect = new Vector3(direction.X, direction.Y, direction.Z);

        RaycastHit hit;
        var flags = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
        var isLoSBlocked =
            Framework.Instance()->BGCollisionModule->RaycastMaterialFilter(&hit, &originVect, &directionVect, distance,
                1, flags);

        return isLoSBlocked == false;
    }

    private void UpdateLastTargetAndQuestVariables(IGameObject? target)
    {
        _lastTargetId = target?.GameObjectId;
        _previousQuestVariables = _currentFight!.Data.ElementId != null
            ? _questFunctions.GetQuestProgressInfo(_currentFight.Data.ElementId)?.Variables
            : null;
        /*
        _logger.LogTrace("UpdateTargetData: {TargetId}; {QuestVariables}",
            target?.GameObjectId.ToString("X8", CultureInfo.InvariantCulture) ?? "null",
            _previousQuestVariables != null ? string.Join(", ", _previousQuestVariables) : "null");
        */
    }

    public void Stop(string label)
    {
        using var scope = _logger.BeginScope(label);
        if (_currentFight != null)
        {
            _logger.LogInformation("Stopping current fight");
            _currentFight.Module.Stop();
        }

        _currentFight = null;
        _wasInCombat = false;
    }

    private void TerritoryChanged(ushort territoryId) => Stop("TerritoryChanged");

    public void Dispose()
    {
        _clientState.TerritoryChanged -= TerritoryChanged;
        Stop("Dispose");
    }

    private sealed class CurrentFight
    {
        public required ICombatModule Module { get; init; }
        public required CombatData Data { get; init; }
        public required DateTime LastDistanceCheck { get; set; }
    }

    public sealed class CombatData
    {
        public required ElementId? ElementId { get; init; }
        public required int Sequence { get; init; }
        public required IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; init; }
        public required EEnemySpawnType SpawnType { get; init; }
        public required List<uint> KillEnemyDataIds { get; init; }
        public required List<ComplexCombatData> ComplexCombatDatas { get; init; }
        public required CombatItemUse? CombatItemUse { get; init; }

        public HashSet<int> CompletedComplexDatas { get; } = new();
    }

    public enum EStatus
    {
        NotStarted,
        InCombat,
        Moving,
        Complete,
    }
}
