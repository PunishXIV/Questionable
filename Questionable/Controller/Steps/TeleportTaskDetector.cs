using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller.Steps;

/// <summary>
///     Identifies upcoming tasks that will teleport the player via an aetheryte or a quest item.
/// </summary>
internal static class TeleportTaskDetector
{
    public static bool IsUpcomingTeleport(ITask task, uint currentTerritoryId)
    {
        if (task is AetheryteShortcut.Task aetheryte)
            return aetheryte.ExpectedTerritoryId != currentTerritoryId;

        return task is UseItem.IUseItemBase;
    }
}
