// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.AutoGen.Lua;

namespace Questionable.AutoGen.Analysis;

/// <summary>
///     Recovers sequence structure from a parsed quest script.
///     <para>
///         Quest scripts are template-generated, so the helper functions have a predictable shape:
///         <c>IsAcceptEvent</c> gates which actors are targetable per sequence, <c>GetNpcTradeItems</c> and
///         <c>getNpcTradeItemInfo</c> pair a sequence with the actor and item involved in a handover, and the
///         <c>OnScene*</c> bodies mention <c>QuestOffer</c>/<c>QuestAccepted</c> or
///         <c>QuestCompleted</c>/<c>QuestReward</c> for the accept and turn-in scenes.
///     </para>
///     <para>
///         The pairing is positional: within one function the script tests <c>GetQuestSequence() == SEQ_n</c>
///         and then references the actor for that branch, so references between one <c>SEQ_</c> symbol and the
///         next are attributed to that sequence. Because that over-collects on branchy functions, every
///         function votes and only the best-supported actors for a sequence are kept. This is a heuristic over
///         the constant stream, not an evaluation of the bytecode.
///     </para>
/// </summary>
public sealed class QuestScriptAnalysis
{
    // Function names are compared case-insensitively: the same helper is spelled getNpcTradeItemInfo in some
    // scripts and GetNpcTradeItemInfo in others.
    private static readonly string[] SequenceMappingFunctions =
    [
        "IsAcceptEvent",
        "IsAnnounce",
        "GetNpcTradeItems",
        "getNpcTradeItemInfo",
        "GetTodoArgs",
        "GetEventItems",
        "IsEventItemUsable"
    ];

    private static readonly string[] TradeFunctions = ["GetNpcTradeItems", "getNpcTradeItemInfo"];

    private readonly Dictionary<byte, Dictionary<string, int>> _targetVotes = [];
    private readonly Dictionary<byte, SequenceHint> _hints = [];

    private QuestScriptAnalysis()
    {
    }

    public IReadOnlyDictionary<byte, SequenceHint> Hints => _hints;

    /// <summary>Scene function names that offer or accept the quest.</summary>
    public IReadOnlyList<string> AcceptScenes => _acceptScenes;

    /// <summary>Scene function names that complete the quest or hand out rewards.</summary>
    public IReadOnlyList<string> CompleteScenes => _completeScenes;

    public bool HasScript { get; private init; }

    /// <summary>
    ///     Whether the script declares <c>IsEventItemUsable</c>. Quests that hand their item to an NPC do not;
    ///     quests where you use the item on something do.
    /// </summary>
    public bool UsesEventItems { get; private set; }

    private readonly List<string> _acceptScenes = [];
    private readonly List<string> _completeScenes = [];

    public static QuestScriptAnalysis Empty() => new() { HasScript = false };

    public static QuestScriptAnalysis Analyze(LuaProto root, QuestSymbols symbols)
    {
        QuestScriptAnalysis analysis = new() { HasScript = true };

        foreach (LuaProto function in EnumerateNamedFunctions(root))
        {
            string name = function.Name!;

            // A script that can use event items declares this; without it, items are handed over, not used.
            bool isUsableItems = string.Equals(name, "IsEventItemUsable", StringComparison.OrdinalIgnoreCase);
            if (isUsableItems)
                analysis.UsesEventItems = true;

            if (SequenceMappingFunctions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                analysis.MapSequences(function, symbols,
                    isTrade: TradeFunctions.Contains(name, StringComparer.OrdinalIgnoreCase),
                    isHeldItems: string.Equals(name, "GetEventItems", StringComparison.OrdinalIgnoreCase),
                    isUsableItems: isUsableItems);
            }

            if (!name.StartsWith("OnScene", StringComparison.Ordinal))
                continue;

            HashSet<string> called = [.. LuaSymbolWalk.Deep(function)];
            if (called.Contains("QuestOffer") || called.Contains("QuestAccepted"))
                analysis._acceptScenes.Add(name);
            if (called.Contains("QuestCompleted") || called.Contains("QuestReward"))
                analysis._completeScenes.Add(name);
        }

        analysis.KeepBestSupportedTargets();
        return analysis;
    }

    private void MapSequences(LuaProto function, QuestSymbols symbols, bool isTrade, bool isHeldItems,
        bool isUsableItems)
    {
        byte? current = null;
        bool tookActor = false;
        bool tookItem = false;
        string? lastActor = null;

        foreach (string symbol in LuaSymbolWalk.Deep(function))
        {
            if (symbols.SequenceValue(symbol) is { } sequence)
            {
                current = sequence;
                tookActor = false;
                tookItem = false;
                lastActor = null;
                continue;
            }

            if (current is not { } activeSequence)
                continue;

            // The script lists an enemy straight after whatever interaction spawns it: "EOBJECT0 ENEMY0".
            if (QuestSymbols.IsEnemySymbol(symbol) && symbols.EnemyLevelRow(symbol) is > 0)
            {
                HintFor(activeSequence).AddEnemy(lastActor, symbol);
                continue;
            }

            // Only the first actor of a branch is that sequence's primary target; the ones after it are usually
            // actors carried over from earlier sequences that happen to still be targetable. They are kept as
            // candidates anyway, because a "speak with all three" sequence really does need all of them.
            if (symbols.DataId(symbol) is > 0)
            {
                lastActor = symbol;
                HintFor(activeSequence).AddCandidate(symbol);

                if (!tookActor)
                {
                    tookActor = true;
                    Vote(activeSequence, symbol);
                    if (isTrade)
                        HintFor(activeSequence).IsNpcTrade = true;
                }
            }
            else if (isTrade && !tookItem && IsItemSymbol(symbol) && symbols.Value(symbol) is > 0)
            {
                tookItem = true;
                SequenceHint hint = HintFor(activeSequence);
                hint.AddTradeItem(symbol);
                hint.IsNpcTrade = true;
            }
            else if (isHeldItems && IsItemSymbol(symbol) && symbols.Value(symbol) is > 0)
            {
                HintFor(activeSequence).AddHeldItem(symbol);
            }
            else if (isUsableItems && IsItemSymbol(symbol) && symbols.Value(symbol) is > 0)
            {
                HintFor(activeSequence).AddUsableItem(symbol);
            }
        }
    }

    private void Vote(byte sequence, string symbol)
    {
        if (!_targetVotes.TryGetValue(sequence, out Dictionary<string, int>? votes))
        {
            votes = new Dictionary<string, int>(StringComparer.Ordinal);
            _targetVotes[sequence] = votes;
        }

        votes[symbol] = votes.GetValueOrDefault(symbol) + 1;
    }

    /// <summary>Keeps only the actors the most functions agree on for each sequence.</summary>
    private void KeepBestSupportedTargets()
    {
        foreach ((byte sequence, Dictionary<string, int> votes) in _targetVotes)
        {
            if (votes.Count == 0)
                continue;

            int best = votes.Values.Max();
            SequenceHint hint = HintFor(sequence);
            foreach (string symbol in votes.Where(x => x.Value == best).Select(x => x.Key).Order(StringComparer.Ordinal))
                hint.AddTarget(symbol);
        }
    }

    private static bool IsItemSymbol(string symbol) =>
        symbol.StartsWith("ITEM", StringComparison.Ordinal) ||
        symbol.StartsWith("EVENTITEM", StringComparison.Ordinal);

    private SequenceHint HintFor(byte sequence)
    {
        if (!_hints.TryGetValue(sequence, out SequenceHint? hint))
        {
            hint = new SequenceHint(sequence);
            _hints[sequence] = hint;
        }

        return hint;
    }

    /// <summary>Every named function in the chunk, including the ones nested inside the script table.</summary>
    private static IEnumerable<LuaProto> EnumerateNamedFunctions(LuaProto root)
    {
        Queue<LuaProto> queue = new();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            LuaProto current = queue.Dequeue();
            if (current.Name != null)
                yield return current;

            foreach (LuaProto nested in current.Protos)
                queue.Enqueue(nested);
        }
    }
}
