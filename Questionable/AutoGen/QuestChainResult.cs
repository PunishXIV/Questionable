// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen;

/// <summary>The quests reachable from a starting quest, and whether the walk stopped at the limit.</summary>
public sealed record QuestChainResult(IReadOnlyList<Quest> Quests, bool Truncated);
