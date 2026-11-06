// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen.Analysis;

internal sealed class QuestSymbols
{
    private readonly Dictionary<string, uint> _values;

    private QuestSymbols(Dictionary<string, uint> values) => _values = values;

    internal static QuestSymbols From(Quest quest)
    {
        Dictionary<string, uint> values = new(StringComparer.Ordinal);
        foreach (var param in quest.QuestParams)
        {
            string instruction = param.ScriptInstruction.ExtractText();
            if (instruction.Length > 0)
                values[instruction] = param.ScriptArg;
        }

        return new QuestSymbols(values);
    }

    internal IReadOnlyDictionary<string, uint> All => _values;

    internal uint? Value(string symbol) => _values.TryGetValue(symbol, out uint value) ? value : null;

    internal uint? DataId(string symbol) =>
        symbol.StartsWith("ACTOR", StringComparison.Ordinal) || symbol.StartsWith("EOBJECT", StringComparison.Ordinal)
            ? Value(symbol)
            : null;

    internal byte? SequenceValue(string symbol)
    {
        if (!symbol.StartsWith("SEQ_", StringComparison.Ordinal))
            return null;

        string suffix = symbol["SEQ_".Length..];
        if (string.Equals(suffix, "FINISH", StringComparison.Ordinal))
            return 255;

        if (byte.TryParse(suffix, out byte sequence))
            return sequence;

        // Some scripts alias a sequence through the symbol table instead of naming it inline.
        return Value(symbol) is { } value && value <= 255 ? (byte)value : null;
    }

    internal uint? EnemyLevelRow(string symbol) =>
        symbol.StartsWith("ENEMY", StringComparison.Ordinal) ? Value(symbol) : null;

    internal static bool IsEnemySymbol(string symbol) => symbol.StartsWith("ENEMY", StringComparison.Ordinal);

    internal uint? SoleEventItem()
    {
        uint? only = null;
        foreach ((string symbol, uint value) in _values)
        {
            if (value == 0 ||
                (!symbol.StartsWith("ITEM", StringComparison.Ordinal) &&
                 !symbol.StartsWith("EVENTITEM", StringComparison.Ordinal)))
            {
                continue;
            }

            if (only != null && only != value)
                return null;

            only = value;
        }

        return only;
    }

    internal uint? LevelRow(string symbol) =>
        symbol.StartsWith("LOC_", StringComparison.Ordinal) ? Value(symbol) : null;

    internal IEnumerable<uint> LevelRowsFor(string actorSymbol)
    {
        string suffix = actorSymbol switch
        {
            _ when actorSymbol.StartsWith("ACTOR", StringComparison.Ordinal) => actorSymbol["ACTOR".Length..],
            _ when actorSymbol.StartsWith("EOBJECT", StringComparison.Ordinal) => actorSymbol["EOBJECT".Length..],
            _ => string.Empty
        };

        if (suffix.Length == 0)
            yield break;

        bool isObject = actorSymbol.StartsWith("EOBJECT", StringComparison.Ordinal);
        string[] candidates = isObject
            ? [$"LOC_EOBJ{suffix}", $"LOC_POS_EOBJ{suffix}", $"LOC_EOBJECT{suffix}"]
            : [$"LOC_ACTOR{suffix}", $"LOC_POS_ACTOR{suffix}"];

        foreach (string candidate in candidates)
        {
            if (Value(candidate) is { } value and > 0)
                yield return value;
        }
    }

    internal IEnumerable<string> DutySymbols =>
        _values.Keys.Where(x =>
            x.StartsWith("QUESTBATTLE", StringComparison.Ordinal) ||
            x.StartsWith("INSTANCEDUNGEON", StringComparison.Ordinal));
}
