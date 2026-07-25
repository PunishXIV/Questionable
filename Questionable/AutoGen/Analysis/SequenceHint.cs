// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Analysis;

/// <summary>What the quest's Lua script says happens at a given sequence.</summary>
public sealed class SequenceHint(byte sequence)
{
    private readonly List<string> _targetSymbols = [];
    private readonly List<string> _candidateSymbols = [];
    private readonly List<string> _tradeItemSymbols = [];

    public byte Sequence { get; } = sequence;

    /// <summary>Symbol names (<c>ACTOR2</c>, <c>EOBJECT0</c>) the script associates with this sequence.</summary>
    public IReadOnlyList<string> TargetSymbols => _targetSymbols;

    /// <summary>
    ///     Every actor the script lists for this sequence, in the order it lists them — not just the primary
    ///     one. A "speak with all three" sequence needs the rest of them.
    /// </summary>
    public IReadOnlyList<string> CandidateSymbols => _candidateSymbols;

    /// <summary><c>ITEM*</c>/<c>EVENTITEM*</c> symbols handed over at this sequence.</summary>
    public IReadOnlyList<string> TradeItemSymbols => _tradeItemSymbols;

    /// <summary>
    ///     Event items the player is holding during this sequence, from <c>GetEventItems</c>. Combined with a
    ///     script that has <c>IsEventItemUsable</c>, these are items to be used on the sequence's target.
    /// </summary>
    public IReadOnlyList<string> HeldItemSymbols => _heldItemSymbols;

    /// <summary>
    ///     Event items <c>IsEventItemUsable</c> permits at this sequence — that is, the sequence the item is
    ///     actually used in, as opposed to the ones it is merely carried through.
    /// </summary>
    public IReadOnlyList<string> UsableItemSymbols => _usableItemSymbols;

    private readonly List<string> _heldItemSymbols = [];
    private readonly List<string> _usableItemSymbols = [];

    public void AddHeldItem(string symbol)
    {
        if (!_heldItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _heldItemSymbols.Add(symbol);
    }

    public void AddUsableItem(string symbol)
    {
        if (!_usableItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _usableItemSymbols.Add(symbol);
    }

    /// <summary>Set when the script trades an item to the NPC rather than just talking to them.</summary>
    public bool IsNpcTrade { get; set; }

    /// <summary>
    ///     Enemies the sequence fights, paired with the actor whose interaction spawns them. <c>Actor</c> is
    ///     <c>null</c> when the script names an enemy with no preceding actor, i.e. the fight starts on its own.
    /// </summary>
    public IReadOnlyList<(string? Actor, string Enemy)> EnemyPairs => _enemyPairs;

    private readonly List<(string? Actor, string Enemy)> _enemyPairs = [];

    public void AddEnemy(string? actor, string enemy)
    {
        if (!_enemyPairs.Contains((actor, enemy)))
            _enemyPairs.Add((actor, enemy));
    }

    public void AddTarget(string symbol)
    {
        if (!_targetSymbols.Contains(symbol, StringComparer.Ordinal))
            _targetSymbols.Add(symbol);
    }

    public void AddCandidate(string symbol)
    {
        if (!_candidateSymbols.Contains(symbol, StringComparer.Ordinal))
            _candidateSymbols.Add(symbol);
    }

    public void AddTradeItem(string symbol)
    {
        if (!_tradeItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _tradeItemSymbols.Add(symbol);
    }
}
