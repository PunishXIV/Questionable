using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Action = Lumina.Excel.Sheets.Action;

namespace Questionable.Functions;

[RegisterSingleton]
internal sealed unsafe class AetheryteFunctions
(
    IServiceProvider serviceProvider,
    ILogger<AetheryteFunctions> logger,
    IDataManager dataManager)
{
    private const uint TeleportAction = 5;
    private const uint ReturnAction = 6;
    private readonly IDataManager _dataManager = dataManager;
    private readonly ILogger<AetheryteFunctions> _logger = logger;

    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public DateTime ReturnRequestedAt { get; set; } = DateTime.MinValue;

    private Dictionary<uint, uint>? _cosmicAethernets;

    public bool IsCosmicAethernet(uint? dataId) => dataId is { } id && CosmicAethernets().ContainsKey(id);

    private Dictionary<uint, uint> CosmicAethernets() => _cosmicAethernets ??= _dataManager.GetExcelSheet<Lumina.Excel.Sheets.WKSAetheryte>()
        .Where(aetheryte => aetheryte.RowId != 0 && aetheryte.ObjectGroup.IsValid)
        .SelectMany(aetheryte => aetheryte.ObjectGroup.Value.Select(crystal => (Crystal: crystal.Unknown0, aetheryte.RowId)))
        .Where(x => x.Crystal != 0)
        .GroupBy(x => x.Crystal)
        .ToDictionary(g => g.Key, g => g.First().RowId);

    // From ICE (Ices-Cosmic-Exploration, AgentWKSMissionEx.IsWKSAetheryteUnlocked): bit (row - 1) is set once unlocked; ClientStructs doesn't name +0xECC.
    public bool? IsCosmicAethernetUnlocked(uint dataId)
    {
        if (!CosmicAethernets().TryGetValue(dataId, out uint row))
            return null;

        WKSManager** manager = WKSManager.StaticAddressPointers.ppInstance;
        if (manager == null || *manager == null)
            return null;

        uint bits = *(uint*)((byte*)*manager + 0xECC);
        return (bits & (1u << (int)(row - 1))) != 0;
    }

    public static bool IsAetheryteUnlocked(uint aetheryteId, out byte subIndex)
    {
        subIndex = 0;

        UIState* uiState = UIState.Instance();
        return uiState != null && uiState->IsAetheryteUnlocked(aetheryteId);
    }

    public bool IsAetheryteUnlocked(EAetheryteLocation aetheryteLocation)
    {
        if (aetheryteLocation.IsFirmamentAetheryte())
            return _serviceProvider.GetRequiredService<QuestFunctions>().IsQuestComplete(new QuestId(3672));
        return IsAetheryteUnlocked((uint)aetheryteLocation, out byte _);
    }

    public bool CanTeleport(EAetheryteLocation aetheryteLocation)
    {
        if ((ushort)aetheryteLocation == PlayerState.Instance()->HomeAetheryteId &&
            ActionManager.Instance()->GetActionStatus(ActionType.Action, ReturnAction) == 0)
        {
            return true;
        }

        return ActionManager.Instance()->GetActionStatus(ActionType.Action, TeleportAction) == 0;
    }

    public bool IsTeleportUnlocked()
    {
        uint unlockLink = _dataManager.GetExcelSheet<Action>()
            .GetRow(5)
            .UnlockLink
            .RowId;
        return UIState.Instance()->IsUnlockLinkUnlocked(unlockLink);
    }

    public bool TeleportAetheryte(uint aetheryteId)
    {
        _logger.LogDebug("Attempting to teleport to aetheryte {AetheryteId}", aetheryteId);
        if (IsAetheryteUnlocked(aetheryteId, out byte subIndex))
        {
            if (aetheryteId == PlayerState.Instance()->HomeAetheryteId &&
                ActionManager.Instance()->GetActionStatus(ActionType.Action, ReturnAction) == 0)
            {
                ReturnRequestedAt = DateTime.Now;
                if (ActionManager.Instance()->UseAction(ActionType.Action, ReturnAction))
                {
                    _logger.LogInformation("Using 'return' for home aetheryte");
                    return true;
                }
            }

            if (ActionManager.Instance()->GetActionStatus(ActionType.Action, TeleportAction) == 0)
            {
                // fallback if return isn't available or (more likely) on a different aetheryte
                _logger.LogInformation("Teleporting to aetheryte {AetheryteId}", aetheryteId);
                return Telepo.Instance()->Teleport(aetheryteId, subIndex);
            }
        }

        return false;
    }

    public bool TeleportAetheryte(EAetheryteLocation aetheryteLocation) => TeleportAetheryte((uint)aetheryteLocation);

    public bool IsFreeAetheryte(EAetheryteLocation aetheryteLocation)
    {
        PlayerState* playerState = PlayerState.Instance();
        return playerState != null &&
               (playerState->FreeAetheryteIds[0] == (uint)aetheryteLocation ||
                playerState->FreeAetheryteIds[1] == (uint)aetheryteLocation);
    }

    public AetheryteRegistrationResult CanRegisterFreeOrFavored(EAetheryteLocation aetheryteLocation)
    {
        PlayerState* playerState = PlayerState.Instance();
        if (playerState == null)
            return AetheryteRegistrationResult.NotPossible;

        // Don't override (or upgrade) a free or favored aetheryte already set here.
        if (IsFreeAetheryte(aetheryteLocation))
            return AetheryteRegistrationResult.NotPossible;

        bool hasFreeSlot = false;
        for (int i = 0; i < playerState->FavouriteAetheryteCount; i++)
        {
            if (playerState->FavouriteAetherytes[i] == (ushort)aetheryteLocation)
                return AetheryteRegistrationResult.NotPossible;

            if (playerState->FavouriteAetherytes[i] == 0)
            {
                hasFreeSlot = true;
                break;
            }
        }

        // probably can't register a ps plus aetheryte on pc, so we don't check for that
        if (playerState->IsPlayerStateFlagSet(PlayerStateFlag.IsLoginSecurityToken) &&
            playerState->FreeAetheryteIds[0] == 0)
        {
            return AetheryteRegistrationResult.FreeDestination;
        }

        return hasFreeSlot
            ? AetheryteRegistrationResult.FavoredDestination
            : AetheryteRegistrationResult.NotPossible;
    }
}
