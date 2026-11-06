namespace Questionable.Data;

internal interface IAetheryteTerritoryProvider
{
    IReadOnlyCollection<uint> AetheryteTerritoryIds { get; }
}
