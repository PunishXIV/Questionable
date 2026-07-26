// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.Model.Common;

namespace Questionable.AutoGen.Generation;

/// <summary>
///     How to get to a zone: an aetheryte to teleport to, plus an aethernet hop when the destination is a city
///     ward that has no aetheryte of its own.
/// </summary>
/// <param name="Caveat">
///     Set when the choice could not be made confidently, e.g. the zone has several aethernet shards but none of
///     them carry a map marker to measure against. The aetheryte is still worth emitting; the hop is left out.
/// </param>
public sealed record TravelShortcut(
    EAetheryteLocation Aetheryte,
    AethernetShortcut? Aethernet,
    string? Caveat = null);
