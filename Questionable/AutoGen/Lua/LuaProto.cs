// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Lua;

/// <summary>A single function prototype out of a Lua 5.1 binary chunk.</summary>
internal sealed class LuaProto
{
    internal string? Source { get; init; }
    internal int LineDefined { get; init; }
    internal IReadOnlyList<uint> Code { get; init; } = [];
    internal IReadOnlyList<object?> Constants { get; init; } = [];
    internal IReadOnlyList<LuaProto> Protos { get; init; } = [];

    /// <summary>Name this function was assigned to, if it could be recovered from the enclosing chunk.</summary>
    public string? Name { get; internal set; }

    internal string? ConstantString(int index) =>
        index >= 0 && index < Constants.Count ? Constants[index] as string : null;
}
