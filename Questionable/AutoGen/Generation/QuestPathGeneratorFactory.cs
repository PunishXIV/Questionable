// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Generation;

/// <summary>
///     Creates <see cref="QuestPathAutoGenerator"/> instances on demand.
///     <para>
///         The game-data indexes are expensive to build and shared across generations, while the author name
///         can change between calls (the plugin reads it from the live configuration), so the factory owns the
///         former and takes the latter per call.
///     </para>
/// </summary>
[RegisterSingleton]
public sealed class QuestPathGeneratorFactory(QuestGameData gameData)
{
    public QuestPathAutoGenerator Create(string author) =>
        new(gameData, string.IsNullOrWhiteSpace(author) ? "Anonymous" : author.Trim());

    /// <summary>
    ///     Generates from a journal quest id, or returns <c>null</c> when no such quest exists. Callers that
    ///     only have an id do not need to touch the Excel sheets themselves.
    /// </summary>
    public QuestPathResult? GenerateById(ushort questId, string author) =>
        gameData.FindByQuestId(questId) is { } quest ? Create(author).Generate(quest) : null;
}
