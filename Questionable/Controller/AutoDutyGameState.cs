namespace Questionable.Controller;

internal readonly record struct AutoDutyGameState(
    bool Available, uint TerritoryId, bool InQueue, bool Loading, bool InDuty, bool Idle);
