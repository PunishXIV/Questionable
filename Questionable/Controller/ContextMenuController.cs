using System.Diagnostics;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
namespace Questionable.Controller;

internal sealed class ContextMenuController : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly IContextMenu _contextMenu;
    private readonly GameFunctions _gameFunctions;
    private readonly IGameGuiAdapter _gameGui;
    private readonly GatheringData _gatheringData;
    private readonly GatheringPointRegistry _gatheringPointRegistry;
    private readonly ILogger<ContextMenuController> _logger;
    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly Configuration _configuration;

    private IEnumerable<ulong> DisabledCharacterIds = [];

    public ContextMenuController(
        IContextMenu contextMenu,
        QuestController questController,
        GatheringPointRegistry gatheringPointRegistry,
        GatheringData gatheringData,
        QuestRegistry questRegistry,
        QuestData questData,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        Configuration configuration,
        IGameGuiAdapter gameGui,
        IChatGui chatGui,
        IClientState clientState,
        ILogger<ContextMenuController> logger)
    {
        _contextMenu = contextMenu;
        _questController = questController;
        _gatheringPointRegistry = gatheringPointRegistry;
        _gatheringData = gatheringData;
        _questRegistry = questRegistry;
        _questData = questData;
        _gameFunctions = gameFunctions;
        _questFunctions = questFunctions;
        _configuration = configuration;
        _gameGui = gameGui;
        _chatGui = chatGui;
        _clientState = clientState;
        _logger = logger;

        _contextMenu.OnMenuOpened += MenuOpened;
    }

    public void Dispose() => _contextMenu.OnMenuOpened -= MenuOpened;

    private void MenuOpened(IMenuOpenedArgs args)
    {
        // no clue why this isn't the actual name, but here we are
        if (args.AddonName != null)
            return;

        if (TryGetHoveredCharacterId(args, out var character))
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
        AddonMaster._CharaSelectListMenu.Character? character)
    {
        bool characterDisabled = DisabledCharacterIds.Contains(character?.Entry->ContentId);
        string characterId = $"FFXIV_CHR{character?.Entry->ContentId:X16}";
        return new()
        {
            Prefix = SeIconChar.Hexagon,
            PrefixColor = 52,
            Name = (characterDisabled ? "ID not found" : "Open Data") + $" (*{characterId[^5..]})",
            IsEnabled = true, //!characterDisabled,
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

        if (!_gatheringPointRegistry.TryGetGatheringPointId(itemId, classJob, out GatheringPointId? _))
        {
            _logger.LogInformation("No gathering point found for {ClassJob}.", classJob);
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
                ((AgentSatisfactionSupply2*)agentSatisfactionSupply)->CalculateTurnInsToNextRank(maxTurnIns));
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
            OnClicked = _ => StartGathering(npcId, itemId, quantityToGather, collectability, classJob),
            IsEnabled = string.IsNullOrEmpty(lockedReasonn)
        });
    }

    private void StartGathering(uint npcId, uint itemId, int quantity, ushort collectability,
        Job classJob)
    {
        SatisfactionSupplyInfo info = (SatisfactionSupplyInfo)_questData.GetAllByIssuerDataId(npcId)
            .Single(x => x is SatisfactionSupplyInfo);
        if (_questRegistry.TryGetQuest(info.QuestId, out Quest? quest))
        {
            QuestSequence sequence = quest.FindSequence(0)!;

            QuestStep switchClassStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.SwitchClass);
            switchClassStep.TargetClass = classJob switch
            {
                Job.MIN => EExtendedClassJob.Miner,
                Job.BTN => EExtendedClassJob.Botanist,
                var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, message: null)
            };

            QuestStep gatherStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.Gather);
            gatherStep.ItemsToGather =
            [
                new()
                {
                    ItemId = itemId,
                    ItemCount = quantity,
                    Collectability = collectability
                }
            ];
            _questController.SetGatheringQuest(quest);
            _questController.StartGatheringQuest("SatisfactionSupply prepare gathering");
        }
        else
            _chatGui.PrintError($"No associated quest ({info.QuestId}).", "Questionable");
    }
}
