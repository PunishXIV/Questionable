using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model;
using Questionable.Model.Questing;
using Action = Lumina.Excel.Sheets.Action;
using BattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;
using ContentFinderCondition = Lumina.Excel.Sheets.ContentFinderCondition;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using Quest = Questionable.Model.Quest;

namespace Questionable.Functions;

internal sealed unsafe class GameFunctions
{
    private delegate void AbandonDutyDelegate(bool a1);

    private readonly QuestFunctions _questFunctions;
    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly IGameGui _gameGui;
    private readonly Configuration _configuration;
    private readonly ILogger<GameFunctions> _logger;
    private readonly AbandonDutyDelegate _abandonDuty;

    private readonly ReadOnlyDictionary<ushort, uint> _territoryToAetherCurrentCompFlgSet;
    private readonly ReadOnlyDictionary<uint, uint> _contentFinderConditionToContentId;

    public GameFunctions(
        QuestFunctions questFunctions,
        IDataManager dataManager,
        IObjectTable objectTable,
        ITargetManager targetManager,
        ICondition condition,
        IClientState clientState,
        IGameGui gameGui,
        Configuration configuration,
        ISigScanner sigScanner,
        ILogger<GameFunctions> logger)
    {
        _questFunctions = questFunctions;
        _dataManager = dataManager;
        _objectTable = objectTable;
        _targetManager = targetManager;
        _condition = condition;
        _clientState = clientState;
        _gameGui = gameGui;
        _configuration = configuration;
        _logger = logger;
        _abandonDuty =
            Marshal.GetDelegateForFunctionPointer<AbandonDutyDelegate>(sigScanner.ScanText(Signatures.AbandonDuty));

        _territoryToAetherCurrentCompFlgSet = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0)
            .Where(x => x.AetherCurrentCompFlgSet.RowId > 0)
            .ToDictionary(x => (ushort)x.RowId, x => x.AetherCurrentCompFlgSet.RowId)
            .AsReadOnly();
        _contentFinderConditionToContentId = dataManager.GetExcelSheet<ContentFinderCondition>()
            .Where(x => x.RowId > 0 && x.Content.RowId > 0)
            .ToDictionary(x => x.RowId, x => x.Content.RowId)
            .AsReadOnly();
    }

    public bool IsFlyingUnlocked(ushort territoryId)
    {
        if (_configuration.Advanced.NeverFly)
            return false;

        if (_questFunctions.IsQuestAccepted(new QuestId(3304)) && _condition[ConditionFlag.Mounted])
        {
            // special quest amaro, not the normal one
            // TODO Check if this also applies to beast tribe mounts
            if (GetMountId() == 198)
                return true;
        }

        var playerState = PlayerState.Instance();
        return playerState != null &&
               _territoryToAetherCurrentCompFlgSet.TryGetValue(territoryId, out uint aetherCurrentCompFlgSet) &&
               playerState->IsAetherCurrentZoneComplete(aetherCurrentCompFlgSet);
    }

    public ushort? GetMountId()
    {
        BattleChara* battleChara = (BattleChara*)(_clientState.LocalPlayer?.Address ?? 0);
        if (battleChara != null && battleChara->Mount.MountId != 0)
            return battleChara->Mount.MountId;
        else
            return null;
    }

    public bool IsFlyingUnlockedInCurrentZone() => IsFlyingUnlocked(_clientState.TerritoryType);

    public bool IsAetherCurrentUnlocked(uint aetherCurrentId)
    {
        var playerState = PlayerState.Instance();
        return playerState != null &&
               playerState->IsAetherCurrentUnlocked(aetherCurrentId);
    }

    public IGameObject? FindObjectByDataId(uint dataId, ObjectKind? kind = null)
    {
        foreach (var gameObject in _objectTable)
        {
            if (gameObject.ObjectKind is ObjectKind.Player or ObjectKind.Companion or ObjectKind.MountType
                or ObjectKind.Retainer or ObjectKind.Housing)
                continue;

            // multiple objects in the object table can share the same data id for gathering points; only one of those
            // (at most) is visible
            if (gameObject is { ObjectKind: ObjectKind.GatheringPoint, IsTargetable: false })
                continue;

            if (gameObject.DataId == dataId && (kind == null || kind.Value == gameObject.ObjectKind))
            {
                return gameObject;
            }
        }

        _logger.LogWarning("Could not find GameObject with dataId {DataId}", dataId);
        return null;
    }

    public bool InteractWith(uint dataId, ObjectKind? kind = null)
    {
        IGameObject? gameObject = FindObjectByDataId(dataId, kind);
        if (gameObject != null)
            return InteractWith(gameObject);

        _logger.LogDebug("Game object is null");
        return false;
    }

    public bool InteractWith(IGameObject gameObject)
    {
        _logger.LogInformation("Setting target with {DataId} to {ObjectId}", gameObject.DataId, gameObject.EntityId);
        _targetManager.Target = null;
        _targetManager.Target = gameObject;

        if (gameObject.ObjectKind == ObjectKind.GatheringPoint)
        {
            TargetSystem.Instance()->OpenObjectInteraction((GameObject*)gameObject.Address);
            _logger.LogInformation("Interact result: (none) for GatheringPoint");
            return true;
        }
        else
        {
            long result = (long)TargetSystem.Instance()->InteractWithObject((GameObject*)gameObject.Address, false);

            _logger.LogInformation("Interact result: {Result}", result);
            return result != 7 && result > 0;
        }
    }

    public bool UseItem(uint itemId)
    {
        long result = AgentInventoryContext.Instance()->UseItem(itemId);
        _logger.LogInformation("UseItem result: {Result}", result);

        return result == 0;
    }

    public bool UseItem(uint dataId, uint itemId)
    {
        IGameObject? gameObject = FindObjectByDataId(dataId);
        if (gameObject != null)
        {
            _targetManager.Target = gameObject;
            long result = AgentInventoryContext.Instance()->UseItem(itemId);

            _logger.LogInformation("UseItem result on {DataId}: {Result}", dataId, result);
            return result is 0 or 1;
        }

        return false;
    }

    public bool UseItemOnGround(uint dataId, uint itemId)
    {
        IGameObject? gameObject = FindObjectByDataId(dataId);
        if (gameObject != null)
        {
            Vector3 position = gameObject.Position;
            return ActionManager.Instance()->UseActionLocation(ActionType.KeyItem, itemId, location: &position);
        }

        return false;
    }

    public bool UseItemOnPosition(Vector3 position, uint itemId)
    {
        return ActionManager.Instance()->UseActionLocation(ActionType.KeyItem, itemId, location: &position);
    }

    public bool UseAction(EAction action)
    {
        uint actionId = (uint)action & 0xFFFF;
        ActionType actionType = ((uint)action & 0x10000) == 0x10000 ? ActionType.GeneralAction : ActionType.Action;
        if (actionType == ActionType.Action)
            actionId = ActionManager.Instance()->GetAdjustedActionId(actionId);

        if (ActionManager.Instance()->GetActionStatus(actionType, actionId) == 0)
        {
            bool result = ActionManager.Instance()->UseAction(actionType, actionId);
            _logger.LogInformation("UseAction {Action} (adjusted: {AdjustedActionId}) result: {Result}", action,
                actionId, result);

            return result;
        }

        return false;
    }

    public bool UseAction(IGameObject gameObject, EAction action, bool checkCanUse = true)
    {
        uint actionId = (uint)action & 0xFFFF;
        ActionType actionType = ((uint)action & 0x10000) == 0x10000 ? ActionType.GeneralAction : ActionType.Action;
        if (actionType == ActionType.GeneralAction)
        {
            _logger.LogWarning("Can not use general action {Action} on target {Target}", action, gameObject);
            return false;
        }

        actionId = ActionManager.Instance()->GetAdjustedActionId(actionId);
        if (checkCanUse && !ActionManager.CanUseActionOnTarget(actionId, (GameObject*)gameObject.Address))
        {
            _logger.LogWarning("Can not use action {Action} (adjusted: {AdjustedActionId}) on target {Target}", action,
                actionId, gameObject);
            return false;
        }

        Action actionRow = _dataManager.GetExcelSheet<Action>().GetRow(actionId);
        _targetManager.Target = gameObject;
        if (ActionManager.Instance()->GetActionStatus(actionType, actionId, gameObject.GameObjectId) == 0)
        {
            bool result;
            if (actionRow.TargetArea)
            {
                Vector3 position = gameObject.Position;
                result = ActionManager.Instance()->UseActionLocation(actionType, actionId,
                    location: &position);
                _logger.LogInformation(
                    "UseAction {Action} (adjusted: {AdjustedActionId}) on target area {Target} result: {Result}",
                    action, actionId, gameObject, result);
            }
            else
            {
                result = ActionManager.Instance()->UseAction(actionType, actionId, gameObject.GameObjectId);
                _logger.LogInformation(
                    "UseAction {Action} (adjusted: {AdjustedActionId}) on target {Target} result: {Result}", action,
                    actionId, gameObject, result);
            }

            return result;
        }

        return false;
    }

    public bool IsObjectAtPosition(uint dataId, Vector3 position, float distance)
    {
        IGameObject? gameObject = FindObjectByDataId(dataId);
        return gameObject != null && (gameObject.Position - position).Length() < distance;
    }

    public bool HasStatusPreventingMount()
    {
        if (_condition[ConditionFlag.Swimming] && !IsFlyingUnlockedInCurrentZone())
            return true;

        // company chocobo is locked
        var playerState = PlayerState.Instance();
        if (playerState != null && !playerState->IsMountUnlocked(1))
            return true;

        var localPlayer = _clientState.LocalPlayer;
        if (localPlayer == null)
            return false;

        var battleChara = (BattleChara*)localPlayer.Address;
        StatusManager* statusManager = battleChara->GetStatusManager();
        if (statusManager->HasStatus(1151) ||
            statusManager->HasStatus(1945)) // hoofing it
            return true;

        return HasCharacterStatusPreventingMountOrSprint();
    }

    public bool HasStatusPreventingSprint() => HasCharacterStatusPreventingMountOrSprint();

    private bool HasCharacterStatusPreventingMountOrSprint()
    {
        var localPlayer = _clientState.LocalPlayer;
        if (localPlayer == null)
            return false;

        var battleChara = (BattleChara*)localPlayer.Address;
        StatusManager* statusManager = battleChara->GetStatusManager();
        return statusManager->HasStatus(565) ||
               statusManager->HasStatus(404) ||
               statusManager->HasStatus(416) ||
               statusManager->HasStatus(2729) ||
               statusManager->HasStatus(2730);
    }

    public bool HasStatus(EStatus statusId)
    {
        var localPlayer = _clientState.LocalPlayer;
        if (localPlayer == null)
            return false;

        var battleChara = (BattleChara*)localPlayer.Address;
        StatusManager* statusManager = battleChara->GetStatusManager();
        return statusManager->HasStatus((uint)statusId);
    }

    public static bool RemoveStatus(EStatus statusId)
    {
        return StatusManager.ExecuteStatusOff((uint)statusId);
    }

    public bool Mount()
    {
        if (_condition[ConditionFlag.Mounted])
            return true;

        var playerState = PlayerState.Instance();
        if (playerState != null && _configuration.General.MountId != 0 &&
            playerState->IsMountUnlocked(_configuration.General.MountId))
        {
            if (ActionManager.Instance()->GetActionStatus(ActionType.Mount, _configuration.General.MountId) == 0)
            {
                _logger.LogDebug("Attempting to use preferred mount...");
                if (ActionManager.Instance()->UseAction(ActionType.Mount, _configuration.General.MountId))
                {
                    _logger.LogInformation("Using preferred mount");
                    return true;
                }

                return false;
            }
        }
        else
        {
            if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 9) == 0)
            {
                _logger.LogDebug("Attempting to use mount roulette...");
                if (ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9))
                {
                    _logger.LogInformation("Using mount roulette");
                    return true;
                }

                return false;
            }
        }

        return false;
    }

    public bool Unmount()
    {
        if (!_condition[ConditionFlag.Mounted])
            return true;

        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 23) == 0)
        {
            _logger.LogDebug("Attempting to unmount...");
            if (ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23))
            {
                _logger.LogInformation("Unmounted");
                return true;
            }

            return false;
        }
        else
        {
            _logger.LogWarning("Can't unmount right now?");
            return false;
        }
    }

    public void OpenDutyFinder(uint contentFinderConditionId)
    {
        if (_contentFinderConditionToContentId.TryGetValue(contentFinderConditionId, out uint contentId))
        {
            if (UIState.IsInstanceContentUnlocked(contentId))
                AgentContentsFinder.Instance()->OpenRegularDuty(contentFinderConditionId);
            else
                _logger.LogError(
                    "Trying to access a locked duty (cf: {ContentFinderId}, content: {ContentId})",
                    contentFinderConditionId, contentId);
        }
        else
            _logger.LogError("Could not find content for content finder condition (cf: {ContentFinderId})",
                contentFinderConditionId);
    }

    /// <summary>
    /// Ensures characters like '-' are handled equally in both strings.
    /// </summary>
    public static bool GameStringEquals(string? a, string? b)
    {
        if (a == null)
            return b == null;

        if (b == null)
            return false;

        return a.ReplaceLineEndings().Replace('\u2013', '-') == b.ReplaceLineEndings().Replace('\u2013', '-');
    }

    public bool IsOccupied()
    {
        if (!_clientState.IsLoggedIn || _clientState.LocalPlayer == null)
            return true;

        if (IsLoadingScreenVisible())
            return true;

        if (_condition[ConditionFlag.Crafting])
        {
            if (!AgentRecipeNote.Instance()->IsAgentActive())
                return true;

            if (!_condition[ConditionFlag.PreparingToCraft])
                return true;
        }

        if (_condition[ConditionFlag.Unconscious] &&
            _condition[ConditionFlag.SufferingStatusAffliction63] &&
            _clientState.TerritoryType == SinglePlayerDuty.SpecialTerritories.Lahabrea)
            return false; // needed to process the tasks

        return _condition[ConditionFlag.Occupied] || _condition[ConditionFlag.Occupied30] ||
               _condition[ConditionFlag.Occupied33] || _condition[ConditionFlag.Occupied38] ||
               _condition[ConditionFlag.Occupied39] || _condition[ConditionFlag.OccupiedInEvent] ||
               _condition[ConditionFlag.OccupiedInQuestEvent] || _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
               _condition[ConditionFlag.Casting] || _condition[ConditionFlag.MountOrOrnamentTransition] ||
               _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] ||
               _condition[ConditionFlag.Jumping61] || _condition[ConditionFlag.ExecutingGatheringAction] ||
               _condition[ConditionFlag.Jumping];
    }

    public bool IsOccupiedWithCustomDeliveryNpc(Quest? currentQuest)
    {
        // not a supply quest?
        if (currentQuest is not { Info: SatisfactionSupplyInfo })
            return false;

        if (_targetManager.Target == null || _targetManager.Target.DataId != currentQuest.Info.IssuerDataId)
            return false;

        if (!AgentSatisfactionSupply.Instance()->IsAgentActive())
            return false;

        var flags = _condition.AsReadOnlySet().ToHashSet();
        flags.Remove(ConditionFlag.InDutyQueue); // irrelevant
        return flags.Count == 2 &&
               flags.Contains(ConditionFlag.NormalConditions) &&
               flags.Contains(ConditionFlag.OccupiedInQuestEvent);
    }

    public bool IsLoadingScreenVisible()
    {
        if (_gameGui.TryGetAddonByName("FadeMiddle", out AtkUnitBase* fade) && LAddon.IsAddonReady(fade) &&
            fade->IsVisible)
            return true;

        if (_gameGui.TryGetAddonByName("FadeBack", out fade) && LAddon.IsAddonReady(fade) && fade->IsVisible)
            return true;

        if (_gameGui.TryGetAddonByName("NowLoading", out fade) && LAddon.IsAddonReady(fade) && fade->IsVisible)
            return true;

        return false;
    }

    public int GetFreeInventorySlots()
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return 0;

        int slots = 0;
        for (InventoryType inventoryType = InventoryType.Inventory1;
             inventoryType <= InventoryType.Inventory4;
             ++inventoryType)
        {
            InventoryContainer* inventoryContainer = inventoryManager->GetInventoryContainer(inventoryType);
            if (inventoryContainer == null)
                continue;

            for (int i = 0; i < inventoryContainer->Size; ++i)
            {
                InventoryItem* item = inventoryContainer->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                    ++slots;
            }
        }

        return slots;
    }

    /// <summary>
    /// Abandons <em>some</em> quest battles/duties; but not all? Useful for debugging some quest battle/vbm related
    /// issues.
    /// </summary>
    public void AbandonDuty() => _abandonDuty(false);

    public IReadOnlyList<uint> GetUnlockLinks()
    {
        UIState* uiState = UIState.Instance();
        if (uiState == null)
        {
            _logger.LogError("Could not query unlock links");
            return [];
        }

        List<uint> unlockedUnlockLinks = [];
        for (uint unlockLink = 0; unlockLink < uiState->UnlockLinkBitmask.Length * 8; ++unlockLink)
        {
            if (uiState->IsUnlockLinkUnlocked(unlockLink))
                unlockedUnlockLinks.Add(unlockLink);
        }

        _logger.LogInformation("Unlocked unlock links: {UnlockedUnlockLinks}", string.Join(", ", unlockedUnlockLinks));
        return unlockedUnlockLinks;
    }

#if false
    private byte ExecuteCommand(int id, int a, int b, int c, int d)
    {
        // Initiate Leve:    804 1794 [1]  0 0 // with [1] = extra difficulty levels
        //                   705 2    1794 0 0
        //                   801 0    0    0 0
        // Abandon:          805 1794 0    0 0
        // Retry button:     803 1794 0 0 0
        return 0;
    }
#endif

    private static class Signatures
    {
        internal const string AbandonDuty = "E8 ?? ?? ?? ?? 41 B2 01 EB 39";
    }
}
