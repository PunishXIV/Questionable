// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Generation;

[RegisterSingleton]
internal sealed class QuestPathGeneratorFactory(QuestGameData gameData)
{
    internal QuestPathAutoGenerator Create(string author) =>
        new(gameData, string.IsNullOrWhiteSpace(author) ? "Anonymous" : author.Trim());

    internal QuestPathResult? GenerateById(ushort questId, string author) =>
        gameData.FindByQuestId(questId) is { } quest ? Create(author).Generate(quest) : null;
}
