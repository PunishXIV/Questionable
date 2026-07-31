// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen.Analysis;

/// <summary>
///     The <c>Quest.QuestParams</c> array is the symbol table the quest's Lua script is compiled against:
///     every <c>ACTOR0</c>, <c>SEQ_3</c>, <c>LOC_ACTOR1</c> or <c>EVENTITEM0</c> the script names resolves
///     to a row id through this table.
/// </summary>
public sealed class QuestSymbols
{
    private readonly Dictionary<string, uint> _values;

    private QuestSymbols(Dictionary<string, uint> values) => _values = values;

    public static QuestSymbols From(Quest quest)
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

    public IReadOnlyDictionary<string, uint> All => _values;

    public uint? Value(string symbol) => _values.TryGetValue(symbol, out uint value) ? value : null;

    /// <summary>Data id behind an <c>ACTOR</c>/<c>EOBJECT</c> symbol name.</summary>
    public uint? DataId(string symbol) =>
        symbol.StartsWith("ACTOR", StringComparison.Ordinal) || symbol.StartsWith("EOBJECT", StringComparison.Ordinal)
            ? Value(symbol)
            : null;

    /// <summary>
    ///     Sequence byte a <c>SEQ_*</c> symbol stands for. These are runtime globals rather than
    ///     <c>QuestParams</c> entries, so the number comes from the symbol name: <c>SEQ_3</c> is sequence 3 and
    ///     <c>SEQ_FINISH</c> is the terminal sequence 255.
    /// </summary>
    public byte? SequenceValue(string symbol)
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

    /// <summary>
    ///     Level row id behind an <c>ENEMY*</c> symbol. Unlike actors, these resolve to a <c>Level</c> row whose
    ///     <c>Object</c> is the BNpcBase id the plugin matches enemies on.
    /// </summary>
    public uint? EnemyLevelRow(string symbol) =>
        symbol.StartsWith("ENEMY", StringComparison.Ordinal) ? Value(symbol) : null;

    public static bool IsEnemySymbol(string symbol) => symbol.StartsWith("ENEMY", StringComparison.Ordinal);

    /// <summary>
    ///     The quest's single event item, when it has exactly one. "Kill things until three of these drop"
    ///     objectives point the fight at it; more than one item and there is no telling which the drop is.
    /// </summary>
    public uint? SoleEventItem()
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

    /// <summary>Level row id backing a <c>LOC_ACTOR*</c>/<c>LOC_POS_*</c>/<c>LOC_EOBJ*</c> symbol.</summary>
    public uint? LevelRow(string symbol) =>
        symbol.StartsWith("LOC_", StringComparison.Ordinal) ? Value(symbol) : null;

    /// <summary>The <c>LOC_</c> symbols that describe where an <c>ACTOR</c>/<c>EOBJECT</c> symbol stands.</summary>
    public IEnumerable<uint> LevelRowsFor(string actorSymbol)
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

    public IEnumerable<string> DutySymbols =>
        _values.Keys.Where(x =>
            x.StartsWith("QUESTBATTLE", StringComparison.Ordinal) ||
            x.StartsWith("INSTANCEDUNGEON", StringComparison.Ordinal));
}
