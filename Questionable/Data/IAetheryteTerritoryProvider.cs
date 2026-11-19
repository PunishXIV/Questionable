namespace Questionable.Data;

/// <summary>
///     Read-only view of the territories that contain an aetheryte. Exposed as an interface so
///     validators (and other consumers that only need this slice of <see cref="AetheryteData"/>)
///     can be unit-tested without dragging in the full Dalamud-backed implementation.
/// </summary>
internal interface IAetheryteTerritoryProvider
{
    IReadOnlyCollection<uint> AetheryteTerritoryIds { get; }
}
