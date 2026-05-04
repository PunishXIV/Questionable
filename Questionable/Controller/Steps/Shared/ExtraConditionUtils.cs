using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal sealed class ExtraConditionUtils(IClientState clientState, IObjectTable objectTable)
{
    private readonly IClientState _clientState = clientState;
    private readonly IObjectTable _objectTable = objectTable;

    public bool MatchesExtraCondition(EExtraSkipCondition skipCondition)
    {
        Vector3? position = _objectTable[0]?.Position;
        return position != null &&
               _clientState.TerritoryType != 0 &&
               MatchesExtraCondition(skipCondition, position.Value, _clientState.TerritoryType);
    }

    public static bool MatchesExtraCondition(EExtraSkipCondition skipCondition, Vector3 position, uint territoryType)
    {
        return skipCondition switch
        {
            EExtraSkipCondition.WakingSandsMainArea => territoryType == 212 && position.X < 24,
            EExtraSkipCondition.WakingSandsSolar => territoryType == 212 && position.X >= 24,
            EExtraSkipCondition.RisingStonesSolar => territoryType == 351 && position.Z <= -28,
            EExtraSkipCondition.RoguesGuild => territoryType == 129 && position.Y <= -115,
            EExtraSkipCondition.NotRoguesGuild => territoryType == 129 && position.Y > -115,
            EExtraSkipCondition.DockStorehouse => territoryType == 137 && position.Y <= -20,
            var _ => throw new ArgumentOutOfRangeException(nameof(skipCondition), skipCondition, null)
        };
    }
}
