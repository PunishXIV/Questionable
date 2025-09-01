using Questionable.Model.Questing;
using System.Globalization;
using System.Numerics;

namespace Questionable.Controller.Steps.Movement;

internal sealed record MoveTask(
    ushort TerritoryId,
    Vector3 Destination,
    bool? Mount = null,
    float? StopDistance = null,
    uint? DataId = null,
    bool DisableNavmesh = false,
    bool? Sprint = null,
    bool Fly = false,
    bool Land = false,
    bool IgnoreDistanceToObject = false,
    bool RestartNavigation = true,
    EInteractionType InteractionType = EInteractionType.None) : ITask
{
    public MoveTask(QuestStep step, Vector3 destination)
        : this(step.TerritoryId,
            destination,
            step.Mount,
            step.CalculateActualStopDistance(),
            step.DataId,
            step.DisableNavmesh,
            step.Sprint,
            step.Fly == true,
            step.Land == true,
            step.IgnoreDistanceToObject == true,
            step.RestartNavigationIfCancelled != false,
            step.InteractionType)
    {
    }

    public bool ShouldRedoOnInterrupt() => true;

    public override string ToString() => $"MoveTo({Destination.ToString("G", CultureInfo.InvariantCulture)})";
}
