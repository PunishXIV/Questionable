using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;

namespace Questionable.Controller.Steps;

/// <summary>
///     Identifies upcoming tasks that will teleport the player via an aetheryte or a quest item.
/// </summary>
internal static class TeleportTaskDetector
{
    public static bool IsUpcomingTeleport(ITask task, uint currentTerritoryId)
    {
        if (task is AetheryteShortcut.Task aetheryte)
        {
            if (aetheryte.ExpectedTerritoryId != currentTerritoryId)
                return true;

            // Same-territory shortcuts with an explicit aetheryte still cast Teleport when the
            // player is far from the destination (e.g. In from the Cold → Camp Broken Glass).
            return aetheryte.TargetAetheryte != EAetheryteLocation.None;
        }

        return task is UseItem.IUseItemBase;
    }
}
