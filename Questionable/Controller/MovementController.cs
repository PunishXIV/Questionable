using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Questionable.Model.Common.Converter;
namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class MovementController
(
    MappingTheRealmIpc mappingTheRealm,
    IClientState clientState,
    GameFunctions gameFunctions,
    ChatFunctions chatFunctions,
    IObjectTable objectTable,
    IFramework framework,
    Lazy<OperationCoordinator> externalOperations,
    ILogger<MovementController> logger) : IDisposable
{
    public const float DefaultVerticalDistance = 1.95f;

    private Task? _route;

    // Stays true until Update has seen how the route ended, so a failed route can't pass as arrival.
    public bool IsPathRunning => _route != null;
    public DestinationData? Destination { get; private set; }
    public NavigationFailureSnapshot? LastFailure { get; private set; }
    public DateTime StartedAt { get; private set; } = DateTime.Now;

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(framework, Stop, logger);

    public void Update()
    {
        if (Destination == null || _route == null)
            return;

        if (externalOperations.Value.HasMovingOperation)
        {
            ClearLocalState();
            return;
        }

        if (_route.IsFaulted || _route.IsCanceled)
        {
            var failure = CreateFailure(_route.IsCanceled ? PathfindingFailureReason.Cancelled : PathfindingFailureReason.ProviderFault,
                "MappingTheRealm could not finish the route", _route.Exception?.GetBaseException());
            ClearLocalState();
            throw failure;
        }

        if (_route.IsCompleted)
            ClearLocalState();
        else if (HasArrived(Destination))
            Stop();
    }

    // MappingTheRealm goes to the exact point; a step's stop distance ends the route early.
    private bool HasArrived(DestinationData destination)
    {
        if (objectTable[0]?.Position is not { } position ||
            (position - destination.Position).Length() >= destination.StopDistance)
            return false;

        if (position.Y - destination.Position.Y <= destination.VerticalStopDistance || destination.DataId == null)
            return true;

        IGameObject? gameObject = gameFunctions.FindObjectByDataId(destination.DataId.Value);
        if (gameObject is ICharacter or IEventObj)
            return Math.Abs(position.Y - gameObject.Position.Y) < DefaultVerticalDistance;
        if (gameObject is { ObjectKind: ObjectKind.Aetheryte })
            return AetheryteConverter.IsLargeAetheryte((EAetheryteLocation)destination.DataId) ||
                   Math.Abs(position.Y - gameObject.Position.Y) < DefaultVerticalDistance;
        return true;
    }

    public void NavigateTo(EMovementType type, uint? dataId, List<Vector3> to, NavigationOptions options) =>
        NavigateTo(type, dataId, to[^1], options);

    public void NavigateTo(EMovementType type, uint? dataId, Vector3 to, NavigationOptions options)
    {
        if (!CanNavigate())
            return;

        if (_route is { IsCompleted: false })
            mappingTheRealm.Stop();

        Destination = new(type, dataId, to,
            options.StopDistance ?? (QuestStep.DefaultStopDistance - 0.2f),
            options.VerticalStopDistance ?? DefaultVerticalDistance);

        if (!mappingTheRealm.IsReady())
        {
            var failure = CreateFailure(PathfindingFailureReason.Unavailable, "MappingTheRealm is not available");
            ClearLocalState();
            throw failure;
        }

        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move");
            chatFunctions.ExecuteCommand("/automove off");
        }

        logger.LogInformation("Moving to {Destination}", Destination);
        _route = mappingTheRealm.Travel(clientState.TerritoryType, to, null);
        StartedAt = DateTime.Now;
    }

    private bool CanNavigate()
    {
        if (externalOperations.Value.HasMovingOperation)
        {
            ClearLocalState();
            return false;
        }

        if (!externalOperations.Value.MayBeMovingCharacter())
            return true;

        var failure = CreateFailure(PathfindingFailureReason.MovementBlocked,
            "Navigation blocked by an external movement provider");
        ClearLocalState();
        throw failure;
    }

    public void RecordFailure(PathfindingException exception, string? quest = null, string? task = null)
    {
        NavigationFailureSnapshot snapshot = exception.Snapshot ?? CaptureFailure(exception, Destination);
        snapshot = snapshot with { Quest = quest ?? snapshot.Quest, Task = task ?? snapshot.Task };
        exception.Snapshot = snapshot;
        LastFailure = snapshot;
    }

    private PathfindingException CreateFailure(PathfindingFailureReason reason, string message,
        Exception? innerException = null)
    {
        var exception = new PathfindingException(reason, message, innerException);
        exception.Snapshot = CaptureFailure(exception, Destination);
        RecordFailure(exception);
        return exception;
    }

    private NavigationFailureSnapshot CaptureFailure(PathfindingException exception, DestinationData? destination) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        Reason = exception.Reason.ToString(),
        Message = exception.Message,
        ExceptionType = exception.GetType().FullName,
        InnerExceptionType = exception.InnerException?.GetType().FullName,
        InnerExceptionMessage = ReadFailureContext(() => exception.InnerException?.Message, null),
        TerritoryId = ReadFailureContext(() => (uint)clientState.TerritoryType, 0u),
        PlayerPosition = ReadFailureContext(() => objectTable[0]?.Position.ToString("G9", CultureInfo.InvariantCulture), null),
        Destination = destination?.Position.ToString("G9", CultureInfo.InvariantCulture),
        DataId = destination?.DataId,
        StopDistance = destination is { StopDistance: var distance } && float.IsFinite(distance) ? distance : null,
        ElapsedMilliseconds = destination == null ? null : (DateTime.Now - StartedAt).TotalMilliseconds,
    };

    private static T ReadFailureContext<T>(Func<T> read, T unknown)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return unknown;
        }
    }

    // MappingTheRealm only cancels a route Questionable started, so other plugins' travel is never cut.
    public void Stop()
    {
        bool ours = _route is { IsCompleted: false };
        ClearLocalState();
        if (!ours)
            return;

        mappingTheRealm.Stop();
        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move [stop]");
            chatFunctions.ExecuteCommand("/automove off");
        }
    }

    private void ClearLocalState()
    {
        Destination = null;
        _route = null;
    }

    public sealed record DestinationData(
        EMovementType MovementType,
        uint? DataId,
        Vector3 Position,
        float StopDistance,
        float VerticalStopDistance);

    public sealed record NavigationOptions
    {
        public float? StopDistance { get; init; }
        public float? VerticalStopDistance { get; init; }
    }

    public enum PathfindingFailureReason
    {
        Unknown,
        Unavailable,
        Cancelled,
        ProviderFault,
        MovementBlocked,
    }

    public sealed class PathfindingException : Exception
    {
        public PathfindingFailureReason Reason { get; }
        internal NavigationFailureSnapshot? Snapshot { get; set; }

        public PathfindingException(PathfindingFailureReason reason, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Reason = reason;
        }

        public PathfindingException()
        {
        }

        public PathfindingException(string message)
            : base(message)
        {
        }

        public PathfindingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
