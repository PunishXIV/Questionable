using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;

namespace Questionable.Controller;

// TODO: refactor — heavy nesting (21 lines indented ≥6 levels, max indent 16 levels).
//       High max indent likely reflects LINQ / method-chain continuations rather than control flow; verify before restructuring.
[RegisterSingleton]
internal sealed class CombatController : IDisposable
{
    public enum EStatus
    {
        NotStarted,
        InCombat,
        Moving,
        Complete
    }

    private const float MaxTargetRange = 55f;
    private const float MaxNameplateRange = 50f;
    private readonly ChatFunctions _chatFunctions;
    private readonly IClientState _clientState;

    private readonly List<ICombatModule> _combatModules;
    private readonly ICondition _condition;
    private readonly ILogger<CombatController> _logger;
    private readonly MovementController _movementController;
    private readonly IObjectTable _objectTable;
    private readonly QuestFunctions _questFunctions;
    private readonly ITargetManager _targetManager;

    private CurrentFight? _currentFight;
    private ulong? _lastTargetId;
    private List<byte>? _previousQuestVariables;
    private bool _wasInCombat;

    public CombatController(
        IEnumerable<ICombatModule> combatModules,
        MovementController movementController,
        ITargetManager targetManager,
        IObjectTable objectTable,
        ICondition condition,
        IClientState clientState,
        QuestFunctions questFunctions,
        ChatFunctions chatFunctions,
        ILogger<CombatController> logger)
    {
        _combatModules = [.. combatModules];
        logger.LogInformation("Combat modules registered (in order): {Modules}",
            string.Join(", ", _combatModules.Select(x => x.GetType().Name)));
        _movementController = movementController;
        _targetManager = targetManager;
        _objectTable = objectTable;
        _condition = condition;
        _clientState = clientState;
        _questFunctions = questFunctions;
        _chatFunctions = chatFunctions;
        _logger = logger;

        _clientState.TerritoryChanged += TerritoryChanged;
    }

    public bool IsRunning => _currentFight != null;

    public void Dispose()
    {
        _clientState.TerritoryChanged -= TerritoryChanged;
        Stop(_L("Dispose"));
    }

    public bool Start(CombatData combatData)
    {
        Stop(_L("Starting combat"));

        ICombatModule? combatModule = null;
        if (combatData.CombatItemUse != null)
            combatModule = _combatModules.OfType<ItemUseModule>().FirstOrDefault(x => x.CanHandleFight(combatData));
        combatModule ??= _combatModules.FirstOrDefault(x => x.CanHandleFight(combatData));
        if (combatModule == null)
            return false;

        if (combatModule.Start(combatData))
        {
            _currentFight = new()
            {
                Module = combatModule,
                Data = combatData,
                LastDistanceCheck = DateTime.Now
            };
            _wasInCombat =
                combatData.SpawnType is EEnemySpawnType.QuestInterruption or EEnemySpawnType.FinishCombatIfAny;
            UpdateLastTargetAndQuestVariables(target: null);
            return true;
        }

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
                                ? QuestFunctions.GetQuestProgressInfo(elementId)
                                : null;

                            if (questProgressInfo != null &&
                                questProgressInfo.Sequence == _currentFight.Data.Sequence &&
                                QuestWorkUtils.HasCompletionFlags(_currentFight.Data.CompletionQuestVariablesFlags) &&
                                QuestWorkUtils.MatchesQuestWork(_currentFight.Data.CompletionQuestVariablesFlags,
                                    questProgressInfo))
                                return EStatus.InCombat;

                            if (questProgressInfo != null &&
                                                                 questProgressInfo.Sequence == _currentFight.Data.Sequence &&
                                                                 _previousQuestVariables != null &&
                                                                 !questProgressInfo.Variables.SequenceEqual(_previousQuestVariables))
                                UpdateLastTargetAndQuestVariables(target: null);
                            else
                                return EStatus.InCombat;
                        }
                    }
                    else
                        _lastTargetId = null;
                }
            }
        }

        IGameObject? target = _targetManager.Target;
        if (target != null)
        {
            int currentTargetPriority = GetKillPriority(target).Priority;
            IGameObject? nextTarget = FindNextTarget();
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
                        SetTarget(target: null);
                    }
                }
            }
            else if (nextTarget != null)
            {
                if (nextTargetPriority > currentTargetPriority || currentTargetPriority == 0)
                    SetTarget(nextTarget);
            }
            else
                SetTarget(target: null);
        }
        else
        {
            IGameObject? nextTarget = FindNextTarget();
            if (nextTarget is { IsDead: false })
                SetTarget(nextTarget);
        }

        if (_condition[ConditionFlag.InCombat])
        {
            _wasInCombat = true;
            return EStatus.InCombat;
        }

        if (_wasInCombat)
            return EStatus.Complete;

        return EStatus.InCombat;
    }

    private IGameObject? FindNextTarget()
    {
        if (_currentFight == null)
            return null;

        // check if any complex combat conditions are fulfilled
        List<ComplexCombatData> complexCombatData = _currentFight.Data.ComplexCombatDatas;
        if (complexCombatData.Count > 0)
        {
            for (int i = 0; i < complexCombatData.Count; ++i)
            {
                if (_currentFight.Data.CompletedComplexDatas.Contains(i))
                    continue;

                ComplexCombatData condition = complexCombatData[i];
                if (condition.RewardItemId != null && condition.RewardItemCount != null)
                {
                    unsafe
                    {
                        InventoryManager* inventoryManager = InventoryManager.Instance();
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
                    QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(questId);
                    if (questWork != null &&
                        QuestWorkUtils.MatchesQuestWork(condition.CompletionQuestVariablesFlags, questWork))
                    {
                        _logger.LogInformation("Complex combat condition fulfilled: QuestWork matches");
                        _currentFight.Data.CompletedComplexDatas.Add(i);
                    }
                }
            }
        }

        Vector3? playerPosition = _objectTable[0]?.Position;
        if (playerPosition == null)
            return null;

        return _objectTable.Select(x => new
        {
            GameObject = x,
            GetKillPriority(x).Priority,
            Distance = Vector3.Distance(x.Position, playerPosition.Value)
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
            if (gameObject.TargetObjectId == _objectTable[0]?.GameObjectId)
                return (rawPriority.Value + 150, reason + "/" + _L("Targeted"));

            // stuff on our enmity list that's not necessarily targeting us
            Hater haters = UIState.Instance()->Hater;
            for (int i = 0; i < haters.HaterCount; ++i)
            {
                HaterInfo hater = haters.Haters[i];
                if (hater.EntityId == gameObject.GameObjectId)
                    return (rawPriority.Value + 125, reason + "/" + _L("Enmity"));
            }
        }

        return (rawPriority.Value, reason);
    }

    private unsafe (int? Priority, string Reason) GetRawKillPriority(IGameObject gameObject)
    {
        if (_currentFight == null)
            return (null, _L("Not Fighting"));

        if (gameObject is IBattleNpc battleNpc)
        {
            if (!_currentFight.Module.CanAttack(battleNpc))
                return (null, _L("Can't attack"));

            if (battleNpc.IsDead)
                return (null, _L("Dead"));

            if (!battleNpc.IsTargetable)
                return (null, _L("Untargetable"));

            List<ComplexCombatData> complexCombatData = _currentFight.Data.ComplexCombatDatas;
            GameObject* gameObjectStruct = (GameObject*)gameObject.Address;
            if (gameObjectStruct->FateId != 0 &&
                gameObject.TargetObjectId != _objectTable[0]?.GameObjectId &&
                _currentFight.Data.SpawnType != EEnemySpawnType.FateEnemies)
                return (null, _L("FATE mob"));

            Vector3 ownPosition = _objectTable[0]?.Position ?? Vector3.Zero;
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

                    if (complexCombatData[i].DataId == GameFunctions.GetBaseID(battleNpc) &&
                        (complexCombatData[i].NameId == null || complexCombatData[i].NameId == battleNpc.NameId))
                    {
                        return (100, "CCD");
                    }
                }
            }
            else
            {
                if ((!expectQuestMarker || gameObjectStruct->NamePlateIconId != 0 || _currentFight.Data.SpawnType == EEnemySpawnType.FateEnemies) &&
                    _currentFight.Data.KillEnemyDataIds.Contains(GameFunctions.GetBaseID(battleNpc)))
                {
                    if (_currentFight.Data.SpawnType == EEnemySpawnType.FateEnemies && !PlayerState.Instance()->IsLevelSynced && EzThrottler.Throttle("lsync", miliseconds: 1000))
                        _chatFunctions.ExecuteCommand("/lsync");
                    return (90, "KED");
                }
            }

            // enemies that we have aggro on
            if (battleNpc.BattleNpcKind is BattleNpcSubKind.BNpcPart or BattleNpcSubKind.Combatant)
            {
                // npc that starts a fate or does turn-ins; not sure why they're marked as hostile
                if (gameObjectStruct->NamePlateIconId is 60093 or 60732)
                    return (null, _L("FATE NPC"));

                return (0, _L("Not part of quest"));
            }

            return (null, _L("Wrong BattleNpcKind"));
        }

        return (null, _L("Not BattleNpc"));
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

            return;
        }

        Vector3? playerPosition = _objectTable[0]?.Position;
        if (playerPosition == null)
            return;

        float distance = Vector3.Distance(playerPosition.Value, target.Position);
        if (distance > MaxTargetRange)
        {
            _logger.LogInformation("Moving to target, distance: {Distance:N2}", distance);
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
        IGameObject? player = _objectTable[0];
        if (player == null)
            return; // uh oh

        float hitboxOffset = player.HitboxRadius + gameObject.HitboxRadius;
        float actualDistance = Vector3.Distance(player.Position, gameObject.Position);
        float maxDistance = ((IPlayerCharacter)player).ClassJob.ValueNullable?.Role is 3 or 4 ? 20f : 2.9f;
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

            MovementController.NavigationOptions options = new()
            {
                StopDistance = maxDistance + hitboxOffset - 0.25f,
                VerticalStopDistance = float.MaxValue,
            };
            if (!useNavmesh)
            {
                _logger.LogInformation("Moving to {TargetName} ({DataId}) to attack", gameObject.Name,
                    GameFunctions.GetBaseID(gameObject));
                _movementController.NavigateTo(EMovementType.Combat, dataId: null, [gameObject.Position], options);
            }
            else
            {
                _logger.LogInformation("Moving to {TargetName} ({DataId}) to attack (with navmesh)", gameObject.Name,
                    GameFunctions.GetBaseID(gameObject));
                _movementController.NavigateTo(EMovementType.Combat, dataId: null, gameObject.Position, options);
            }
        }
    }

    internal unsafe bool IsInLineOfSight(IGameObject target)
    {
        Vector3 sourcePos = _objectTable[0]!.Position;
        sourcePos.Y += 2;

        Vector3 targetPos = target.Position;
        targetPos.Y += 2;

        Vector3 direction = targetPos - sourcePos;
        float distance = direction.Length();

        direction = Vector3.Normalize(direction);

        Vector3 originVect = new(sourcePos.X, sourcePos.Y, sourcePos.Z);
        Vector3 directionVect = new(direction.X, direction.Y, direction.Z);

        RaycastHit hit;
        int* flags = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
        bool isLoSBlocked =
            Framework.Instance()->BGCollisionModule->RaycastMaterialFilter(&hit, &originVect, &directionVect, distance,
                1, flags);

        return !isLoSBlocked;
    }

    private void UpdateLastTargetAndQuestVariables(IGameObject? target)
    {
        _lastTargetId = target?.GameObjectId;
        _previousQuestVariables = _currentFight!.Data.ElementId != null
            ? QuestFunctions.GetQuestProgressInfo(_currentFight.Data.ElementId)?.Variables
            : null;
        /*
        _logger.LogTrace("UpdateTargetData: {TargetId}; {QuestVariables}",
            target?.GameObjectId.ToString("X8", CultureInfo.InvariantCulture) ?? "null",
            _previousQuestVariables != null ? string.Join(", ", _previousQuestVariables) : "null");
        */
    }

    public void Stop(string label)
    {
        using IDisposable? scope = _logger.BeginScope(label);
        if (_currentFight != null)
        {
            _logger.LogInformation("Stopping current fight");
            _currentFight.Module.Stop();
        }

        _currentFight = null;
        _wasInCombat = false;
    }

    private void TerritoryChanged(uint territoryId) => Stop(_L("TerritoryChanged"));

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

        public HashSet<int> CompletedComplexDatas { get; } = [];
    }
}
