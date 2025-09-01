using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using LLib.GameData;
using Microsoft.Extensions.Logging;
using System;

namespace Questionable.Controller.Utils;

internal sealed class PartyWatchDog : IDisposable
{
    private readonly QuestController _questController;
    private readonly IClientState _clientState;
    private readonly IChatGui _chatGui;
    private readonly ILogger<PartyWatchDog> _logger;

    private ushort? _uncheckedTeritoryId;

    public PartyWatchDog(QuestController questController, IClientState clientState, IChatGui chatGui,
        ILogger<PartyWatchDog> logger)
    {
        _questController = questController;
        _clientState = clientState;
        _chatGui = chatGui;
        _logger = logger;

        _clientState.TerritoryChanged += TerritoryChanged;
    }

    private unsafe void TerritoryChanged(ushort newTerritoryId)
    {
        var intendedUse = (ETerritoryIntendedUse)GameMain.Instance()->CurrentTerritoryIntendedUseId;
        switch (intendedUse)
        {
            case ETerritoryIntendedUse.Gaol:
            case ETerritoryIntendedUse.Frontline:
            case ETerritoryIntendedUse.LordOfVerminion:
            case ETerritoryIntendedUse.Diadem:
            case ETerritoryIntendedUse.CrystallineConflict:
            case ETerritoryIntendedUse.Battlehall:
            case ETerritoryIntendedUse.CrystallineConflict2:
            case ETerritoryIntendedUse.DeepDungeon:
            case ETerritoryIntendedUse.TreasureMapDuty:
            case ETerritoryIntendedUse.Diadem2:
            case ETerritoryIntendedUse.RivalWings:
            case ETerritoryIntendedUse.Eureka:
            case ETerritoryIntendedUse.LeapOfFaith:
            case ETerritoryIntendedUse.OceanFishing:
            case ETerritoryIntendedUse.Diadem3:
            case ETerritoryIntendedUse.Bozja:
            case ETerritoryIntendedUse.Battlehall2:
            case ETerritoryIntendedUse.Battlehall3:
            case ETerritoryIntendedUse.LargeScaleRaid:
            case ETerritoryIntendedUse.LargeScaleSavageRaid:
            case ETerritoryIntendedUse.Blunderville:
                StopIfRunning($"Unsupported Area entered ({newTerritoryId})");
                break;

            case ETerritoryIntendedUse.Dungeon:
            case ETerritoryIntendedUse.VariantDungeon:
            case ETerritoryIntendedUse.AllianceRaid:
            case ETerritoryIntendedUse.Trial:
            case ETerritoryIntendedUse.Raid:
            case ETerritoryIntendedUse.Raid2:
            case ETerritoryIntendedUse.SeasonalEvent:
            case ETerritoryIntendedUse.SeasonalEvent2:
            case ETerritoryIntendedUse.CriterionDuty:
            case ETerritoryIntendedUse.CriterionSavageDuty:
                _uncheckedTeritoryId = newTerritoryId;
                _logger.LogInformation("Will check territory {TerritoryId} after loading", newTerritoryId);
                break;
        }
    }

    public unsafe void Update()
    {
        if (_uncheckedTeritoryId == _clientState.TerritoryType && GameMain.Instance()->TerritoryLoadState == 2)
        {
            var groupManager = GroupManager.Instance();
            if (groupManager == null)
                return;

            byte memberCount = groupManager->MainGroup.MemberCount;
            bool isInAlliance = groupManager->MainGroup.IsAlliance;
            _logger.LogDebug("Territory {TerritoryId} with {MemberCount} members, alliance: {IsInAlliance}",
                _uncheckedTeritoryId, memberCount, isInAlliance);
            if (memberCount > 1 || isInAlliance)
                StopIfRunning("Other party members present");

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

    public void Dispose()
    {
        _clientState.TerritoryChanged -= TerritoryChanged;
    }
}
