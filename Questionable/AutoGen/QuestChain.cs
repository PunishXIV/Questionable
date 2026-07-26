// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Lumina.Excel.Sheets;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen;


/// <summary>
///     Walks the prerequisite graph out from a quest, in both directions: the quests it requires
///     (<c>Quest.PreviousQuest</c>) and the quests that require it.
/// </summary>
public static class QuestChain
{
    /// <summary>
    ///     Ceiling on how many paths one <c>--recursive</c> expansion may write. Applied after quests that
    ///     already have a file are dropped, so a chain that is mostly done still reaches the gaps further along
    ///     it rather than spending the budget on quests it is going to skip anyway.
    /// </summary>
    public const int GenerationLimit = 200;

    /// <summary>
    ///     Ceiling on how far the graph walk itself will go. Traversal is only sheet lookups, so this can be
    ///     far more generous than <see cref="GenerationLimit"/>; it exists to stop the story graph running away.
    /// </summary>
    public const int TraversalLimit = 5000;

    public static QuestChainResult Resolve(QuestGameData gameData, Quest root, int maxDepth, int limit)
    {
        Dictionary<uint, Quest> found = new() { [root.RowId] = root };
        Queue<(Quest Quest, int Depth)> queue = new();
        queue.Enqueue((root, 0));
        bool truncated = false;

        while (queue.Count > 0)
        {
            (Quest current, int depth) = queue.Dequeue();
            if (depth >= maxDepth)
                continue;

            foreach (Quest neighbour in gameData.PreviousQuests(current).Concat(gameData.NextQuests(current)))
            {
                if (found.ContainsKey(neighbour.RowId))
                    continue;

                if (found.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                found[neighbour.RowId] = neighbour;
                queue.Enqueue((neighbour, depth + 1));
            }

            if (truncated)
                break;
        }

        return new QuestChainResult(
            [.. found.Values.OrderBy(x => x.RowId & 0xFFFF)],
            truncated);
    }
}
