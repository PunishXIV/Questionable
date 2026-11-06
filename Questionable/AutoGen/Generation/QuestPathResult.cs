// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.Model.Questing;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen.Generation;

internal sealed record QuestPathResult(
    Quest Quest,
    QuestRoot Root,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<QuestStep, string> Provenance);
