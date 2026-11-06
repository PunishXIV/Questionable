// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.Model.Common;

namespace Questionable.AutoGen.Generation;

internal sealed record TravelShortcut(
    EAetheryteLocation Aetheryte,
    AethernetShortcut? Aethernet,
    string? Caveat = null);
