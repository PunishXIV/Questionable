using System.Diagnostics;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Questionable.Model.Questing;
namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class ContextMenuController : IDisposable
{
    private readonly IClientState _clientState;
    private readonly IContextMenu _contextMenu;
    private readonly GameFunctions _gameFunctions;
    private readonly IGameGuiAdapter _gameGui;
    private readonly GatheringData _gatheringData;
    private readonly GatherChillIpc _gatherChillIpc;
    private readonly ILogger<ContextMenuController> _logger;
    private readonly QuestController _questController;
    private readonly QuestFunctions _questFunctions;
    private readonly Configuration _configuration;

    private IEnumerable<ulong> DisabledCharacterIds = [];

    public ContextMenuController(
        IContextMenu contextMenu,
        QuestController questController,
        GatherChillIpc gatherChillIpc,
        GatheringData gatheringData,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        Configuration configuration,
        IGameGuiAdapter gameGui,
        IClientState clientState,
        ILogger<ContextMenuController> logger)
    {
        _contextMenu = contextMenu;
        _questController = questController;
        _gatherChillIpc = gatherChillIpc;
        _gatheringData = gatheringData;
        _gameFunctions = gameFunctions;
        _questFunctions = questFunctions;
        _configuration = configuration;
        _gameGui = gameGui;
        _clientState = clientState;
        _logger = logger;

        _contextMenu.OnMenuOpened += MenuOpened;
    }

    public void Dispose() => _contextMenu.OnMenuOpened -= MenuOpened;

    private void MenuOpened(IMenuOpenedArgs args)
    {
        // The game reports this as a null addon name.
        if (args.AddonName != null)
            return;

        if (TryGetHoveredCharacterId(args, out var characterNullable) && characterNullable is AddonMaster._CharaSelectListMenu.Character character)
        {
            args.AddMenuItem(CreateMenuItem_CharaSelectListMenu(character));
        }
        else if (TryGetHoveredSatisfactionSupplyItemId(out var itemId))
        {
            if (itemId > 1_000_000)
                itemId -= 1_000_000;

            if (itemId >= 500_000)
                itemId -= 500_000;

            if (_gatheringData.TryGetCustomDeliveryNpc(itemId, out uint npcId))
            {
                AddContextMenuEntry_SatisfactionSupply(args, itemId, npcId, Job.MIN, "Mine");
                AddContextMenuEntry_SatisfactionSupply(args, itemId, npcId, Job.BTN, "Harvest");
            }
            else
                _logger.LogDebug("No custom delivery NPC found for item {ItemId}.", itemId);
        }

    }

    private unsafe bool TryGetHoveredSatisfactionSupplyItemId(out uint hoveredItem)
    {
        hoveredItem = 0;
        AgentSatisfactionSupply* agent = AgentSatisfactionSupply.Instance();
        if (agent != null && agent->IsAgentActive() &&
            _gameGui.TryGetAddonByName("SatisfactionSupply", out AddonSatisfactionSupply* addon) &&
            AddonUtils.IsAddonReady(&addon->AtkUnitBase) &&
            addon->HoveredElementIndex is >= 0 and <= 2)
        {
            hoveredItem = agent->Items[addon->HoveredElementIndex].Id;
        }

        return hoveredItem != 0;
    }

    // At 4/5 hearts satisfaction is 540/1080 with 3 deliveries left, 720 with 4, 900 with 5; at 5 hearts it is 0/0.
    private static int TurnInsToNextRank(ushort current, ushort max, int maxTurnIns) =>
        max == 0 ? maxTurnIns : maxTurnIns * (max - current) / max;

    private static bool TryGetHoveredCharacterId(
        IMenuOpenedArgs args,
        out AddonMaster._CharaSelectListMenu.Character? character)
    {
        character = null;
        if (GenericHelpers.TryGetAddonMaster<AddonMaster._CharaSelectListMenu>(out var m))
            if (args.Target is MenuTargetDefault target && m.Characters.TryGetFirst(x => x.IsSelected, out var chara))
                character = chara;
        return character != null;
    }

    private void DisableCharacterId(ulong characterId)
    {
        DisabledCharacterIds = DisabledCharacterIds.Append(characterId);
    }

    private unsafe MenuItem CreateMenuItem_CharaSelectListMenu(
        AddonMaster._CharaSelectListMenu.Character character)
    {
        bool characterDisabled = DisabledCharacterIds.Contains(character.Entry->ContentId);
        string characterId = $"FFXIV_CHR{character.Entry->ContentId:X16}";
        return new()
        {
            Prefix = SeIconChar.Hexagon,
            PrefixColor = 52,
            Name = (characterDisabled ? "ID not found" : "Open Data") + $" (*{characterId[^5..]})",
            IsEnabled = true,
            OnClicked = _1 =>
            {
                if (character != null)
                {
                    var path = Path.Combine(Framework.Instance()->UserPathString, characterId);
                    if (!Path.Exists(path))
                    {
                        DisableCharacterId(character.Entry->ContentId);
                        return;
                    }
                    string homeWorld = ExcelWorldHelper.GetName(character.HomeWorld);
                    FileInfo charNameFile = new(Path.Combine(path, $"_{character.Name}@{homeWorld}"));
                    try
                    {
                        _ = charNameFile.Create();
                    }
                    catch (Exception)
                    {
                    }
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
        };
    }

    private unsafe void AddContextMenuEntry_SatisfactionSupply(IMenuOpenedArgs args, uint itemId, uint npcId, Job classJob,
        string verb)
    {
        Job currentClassJob = (Job)PlayerState.Instance()->CurrentClassJobId;
        if (classJob != currentClassJob)
            return;

        if (!_gatheringData.IsGatherableBy(itemId, classJob) || !_gatherChillIpc.CanGather(itemId))
        {
            _logger.LogInformation("Gather n Chill can't gather {ItemId} as {ClassJob}.", itemId, classJob);
            return;
        }

        ushort collectability = _gatheringData.GetRecommendedCollectability(itemId);
        int quantityToGather = collectability > 0 ? 6 : int.MaxValue;
        if (collectability == 0)
            return;

        AgentSatisfactionSupply* agentSatisfactionSupply = AgentSatisfactionSupply.Instance();
        if (agentSatisfactionSupply->IsAgentActive())
        {
            int maxTurnIns = agentSatisfactionSupply->NpcInfo.SatisfactionRank == 1 ? 3 : 6;
            quantityToGather = Math.Min(agentSatisfactionSupply->NpcData.RemainingAllowances,
                TurnInsToNextRank(agentSatisfactionSupply->NpcData.SatisfactionCur,
                    agentSatisfactionSupply->NpcData.SatisfactionMax, maxTurnIns));
        }
        if (_configuration.Advanced.Debug)
            quantityToGather = 1;

        string lockedReasonn = string.Empty;
        if (!_configuration.Advanced.Debug)
        {
            if (!_questFunctions.IsClassJobUnlocked(classJob))
                lockedReasonn = $"{classJob} not unlocked";
            else if (quantityToGather == 0)
                lockedReasonn = "No allowances";
            else if (quantityToGather > GameFunctions.GetFreeInventorySlots())
                lockedReasonn = "Inventory full";
            else if (_gameFunctions.IsOccupied())
                lockedReasonn = "Can't be used while interacting";
        }

        string name = $"{verb} with Questionable";
        if (!string.IsNullOrEmpty(lockedReasonn))
            name += $" ({lockedReasonn})";

        args.AddMenuItem(new()
        {
            Prefix = SeIconChar.Hyadelyn,
            PrefixColor = 52,
            Name = name,
            OnClicked = _ => _questController.StartGathering(npcId, itemId, classJob, quantityToGather, collectability),
            IsEnabled = string.IsNullOrEmpty(lockedReasonn)
        });
    }
}
