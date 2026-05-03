using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using Microsoft.Extensions.Logging;
using System;
namespace Questionable.Controller.Utils;

internal sealed class PartyWatchDog : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly ILogger<PartyWatchDog> _logger;
    private readonly QuestController _questController;

    private uint? _uncheckedTeritoryId;

    public PartyWatchDog(QuestController questController, IClientState clientState, IChatGui chatGui,
        ILogger<PartyWatchDog> logger)
    {
        _questController = questController;
        _clientState = clientState;
        _chatGui = chatGui;
        _logger = logger;

        _clientState.TerritoryChanged += TerritoryChanged;
    }

    public void Dispose()
    {
        _clientState.TerritoryChanged -= TerritoryChanged;
    }

    private unsafe void TerritoryChanged(uint newTerritoryId)
    {
        TerritoryIntendedUseEnum intendedUse = (TerritoryIntendedUseEnum)GameMain.Instance()->CurrentTerritoryIntendedUseId;
        switch (intendedUse)
        {
            case TerritoryIntendedUseEnum.Gaol:
            case TerritoryIntendedUseEnum.Frontline:
            case TerritoryIntendedUseEnum.Lord_of_Verminion:
            case TerritoryIntendedUseEnum.Diadem:
            case TerritoryIntendedUseEnum.Crystalline_Conflict:
            case TerritoryIntendedUseEnum.Battlehall:
            case TerritoryIntendedUseEnum.Crystalline_Conflict_2:
            case TerritoryIntendedUseEnum.Deep_Dungeon:
            case TerritoryIntendedUseEnum.Treasure_Map_Duty:
            case TerritoryIntendedUseEnum.Diadem_2:
            case TerritoryIntendedUseEnum.Rival_Wings:
            case TerritoryIntendedUseEnum.Eureka:
            case TerritoryIntendedUseEnum.Leap_of_Faith:
            case TerritoryIntendedUseEnum.Ocean_Fishing:
            case TerritoryIntendedUseEnum.Diadem_3:
            case TerritoryIntendedUseEnum.Bozja:
            case TerritoryIntendedUseEnum.Battlehall_2:
            case TerritoryIntendedUseEnum.Battlehall_3:
            case TerritoryIntendedUseEnum.Large_Scale_Raid:
            case TerritoryIntendedUseEnum.Large_Scale_Savage_Raid:
            case TerritoryIntendedUseEnum.Blunderville:
                StopIfRunning($"Unsupported Area entered ({newTerritoryId})");
                break;
            case TerritoryIntendedUseEnum.Dungeon:
            case TerritoryIntendedUseEnum.Variant_Dungeon:
            case TerritoryIntendedUseEnum.Alliance_Raid:
            case TerritoryIntendedUseEnum.Trial:
            case TerritoryIntendedUseEnum.Raid:
            case TerritoryIntendedUseEnum.Raid_2:
            case TerritoryIntendedUseEnum.Seasonal_Event:
            case TerritoryIntendedUseEnum.Seasonal_Event_2:
            case TerritoryIntendedUseEnum.Criterion_Duty:
            case TerritoryIntendedUseEnum.Criterion_Savage_Duty:
                _uncheckedTeritoryId = newTerritoryId;
                _logger.LogInformation("Will check territory {TerritoryId} after loading", newTerritoryId);
                break;
        }
    }

    public unsafe void Update()
    {
        if (_uncheckedTeritoryId == _clientState.TerritoryType && GameMain.Instance()->TerritoryLoadState == 2)
        {
            GroupManager* groupManager = GroupManager.Instance();
            if (groupManager == null)
            {
                return;
            }

            byte memberCount = groupManager->MainGroup.MemberCount;
            bool isInAlliance = groupManager->MainGroup.IsAlliance;
            _logger.LogDebug("Territory {TerritoryId} with {MemberCount} members, alliance: {IsInAlliance}",
                _uncheckedTeritoryId, memberCount, isInAlliance);
            if (memberCount > 1 || isInAlliance)
            {
                StopIfRunning("Other party members present");
            }

            _uncheckedTeritoryId = null;
        }
    }

    private void StopIfRunning(string reason)
    {
        if (_questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual)
        {
            _chatGui.PrintError(
                $"Stopping Questionable: {reason}. If you believe this to be correct, please restart Questionable manually.",
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _questController.Stop(reason);
        }
    }
}
