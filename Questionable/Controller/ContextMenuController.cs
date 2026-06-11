using System;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.GameStructs;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Questionable.Utils;
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

        uint itemId = GetHoveredSatisfactionSupplyItemId();
        if (itemId == 0)
        {
            _logger.LogTrace("Ignoring context menu, no item hovered");
            return;
        }

        if (itemId > 1_000_000)
            itemId -= 1_000_000;

        if (itemId >= 500_000)
            itemId -= 500_000;

        if (_gatheringData.TryGetCustomDeliveryNpc(itemId, out uint npcId))
        {
            AddContextMenuEntry(args, itemId, npcId, Job.MIN, "Mine");
            AddContextMenuEntry(args, itemId, npcId, Job.BTN, "Harvest");
        }
        else
            _logger.LogDebug("No custom delivery NPC found for item {ItemId}.", itemId);
    }

    private unsafe uint GetHoveredSatisfactionSupplyItemId()
    {
        AgentSatisfactionSupply* agent = AgentSatisfactionSupply.Instance();
        if (agent == null || !agent->IsAgentActive())
            return 0;


        if (_gameGui.TryGetAddonByName("SatisfactionSupply", out AddonSatisfactionSupply* addon) &&
            AddonUtils.IsAddonReady(&addon->AtkUnitBase) &&
            addon->HoveredElementIndex is >= 0 and <= 2)
        {
            return agent->Items[addon->HoveredElementIndex].Id;
        }

        return 0;
    }

    private unsafe void AddContextMenuEntry(IMenuOpenedArgs args, uint itemId, uint npcId, Job classJob,
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
                var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, null)
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
