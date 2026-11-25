namespace Questionable.Controller;

internal sealed record NavigationFailureSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ExceptionType { get; init; }
    public string? InnerExceptionType { get; init; }
    public string? InnerExceptionMessage { get; init; }
    public uint TerritoryId { get; init; }
    public string? PlayerPosition { get; init; }
    public string? Destination { get; init; }
    public uint? DataId { get; init; }
    public float? StopDistance { get; init; }
    public double? ElapsedMilliseconds { get; init; }
    public string? Quest { get; init; }
    public string? Task { get; init; }
}
