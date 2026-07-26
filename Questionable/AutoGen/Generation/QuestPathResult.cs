// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Lumina.Excel.Sheets;
using Quest = Lumina.Excel.Sheets.Quest;
using Questionable.Model.Questing;

namespace Questionable.AutoGen.Generation;

/// <summary>
///     A generated path plus the caveats that came with it.
///     <paramref name="Provenance" /> maps each step to the note written into its <c>$</c> dev comment.
/// </summary>
public sealed record QuestPathResult(
    Quest Quest,
    QuestRoot Root,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<QuestStep, string> Provenance);
