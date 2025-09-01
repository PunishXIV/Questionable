using Dalamud.Plugin.Services;
using Questionable.Model.Questing;
using System;
using System.Numerics;

namespace Questionable.Controller.Steps.Shared;

internal sealed class ExtraConditionUtils
{
    private readonly IClientState _clientState;

    public ExtraConditionUtils(IClientState clientState)
    {
        _clientState = clientState;
    }

    public bool MatchesExtraCondition(EExtraSkipCondition skipCondition)
    {
        var position = _clientState.LocalPlayer?.Position;
        return position != null &&
               _clientState.TerritoryType != 0 &&
               MatchesExtraCondition(skipCondition, position.Value, _clientState.TerritoryType);
    }

    public static bool MatchesExtraCondition(EExtraSkipCondition skipCondition, Vector3 position, ushort territoryType)
    {
        return skipCondition switch
        {
            EExtraSkipCondition.WakingSandsMainArea => territoryType == 212 && position.X < 24,
            EExtraSkipCondition.WakingSandsSolar => territoryType == 212 && position.X >= 24,
            EExtraSkipCondition.RisingStonesSolar => territoryType == 351 && position.Z <= -28,
            EExtraSkipCondition.RoguesGuild => territoryType == 129 && position.Y <= -115,
            EExtraSkipCondition.NotRoguesGuild => territoryType == 129 && position.Y > -115,
            EExtraSkipCondition.DockStorehouse => territoryType == 137 && position.Y <= -20,
            _ => throw new ArgumentOutOfRangeException(nameof(skipCondition), skipCondition, null)
        };
    }
}
