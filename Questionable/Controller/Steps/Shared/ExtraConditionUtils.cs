using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal sealed class ExtraConditionUtils(IClientState clientState, IObjectTable objectTable, RedoUtil redoUtil)
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

    public bool MatchesExtraCondition(EExtraSkipCondition skipCondition, Vector3 position, uint territoryType)
    {
        return skipCondition switch
        {
            EExtraSkipCondition.WakingSandsMainArea => territoryType == 212 && position.X < 24,
            EExtraSkipCondition.WakingSandsSolar => territoryType == 212 && position.X >= 24,
            EExtraSkipCondition.RisingStonesSolar => territoryType == 351 && position.Z <= -28,
            EExtraSkipCondition.RoguesGuild => territoryType == 129 && position.Y <= -115,
            EExtraSkipCondition.NotRoguesGuild => territoryType == 129 && position.Y > -115,
            EExtraSkipCondition.DockStorehouse => territoryType == 137 && position.Y <= -20,
            EExtraSkipCondition.CostaDelSol => territoryType == 137 && position.Z > 55 && position.X > 165,
            EExtraSkipCondition.NewGamePlus => redoUtil.IsRedoActive(),
            EExtraSkipCondition.NotNewGamePlus => !redoUtil.IsRedoActive(),
            var _ => throw new ArgumentOutOfRangeException(nameof(skipCondition), skipCondition, message: null)
        };
    }
}
