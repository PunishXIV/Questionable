// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Analysis;

internal sealed class SequenceHint(byte sequence)
{
    private readonly List<string> _targetSymbols = [];
    private readonly List<string> _candidateSymbols = [];
    private readonly List<string> _tradeItemSymbols = [];

    internal byte Sequence { get; } = sequence;

    internal IReadOnlyList<string> TargetSymbols => _targetSymbols;

    internal IReadOnlyList<string> CandidateSymbols => _candidateSymbols;

    internal IReadOnlyList<string> TradeItemSymbols => _tradeItemSymbols;

    internal IReadOnlyList<string> HeldItemSymbols => _heldItemSymbols;

    internal IReadOnlyList<string> UsableItemSymbols => _usableItemSymbols;

    private readonly List<string> _heldItemSymbols = [];
    private readonly List<string> _usableItemSymbols = [];

    internal void AddHeldItem(string symbol)
    {
        if (!_heldItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _heldItemSymbols.Add(symbol);
    }

    internal void AddUsableItem(string symbol)
    {
        if (!_usableItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _usableItemSymbols.Add(symbol);
    }

    internal bool IsNpcTrade { get; set; }

    internal IReadOnlyList<(string? Actor, string Enemy)> EnemyPairs => _enemyPairs;

    private readonly List<(string? Actor, string Enemy)> _enemyPairs = [];

    internal void AddEnemy(string? actor, string enemy)
    {
        if (!_enemyPairs.Contains((actor, enemy)))
            _enemyPairs.Add((actor, enemy));
    }

    internal void AddTarget(string symbol)
    {
        if (!_targetSymbols.Contains(symbol, StringComparer.Ordinal))
            _targetSymbols.Add(symbol);
    }

    internal void AddCandidate(string symbol)
    {
        if (!_candidateSymbols.Contains(symbol, StringComparer.Ordinal))
            _candidateSymbols.Add(symbol);
    }

    internal void AddTradeItem(string symbol)
    {
        if (!_tradeItemSymbols.Contains(symbol, StringComparer.Ordinal))
            _tradeItemSymbols.Add(symbol);
    }
}
