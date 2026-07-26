// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Lua;

/// <summary>A single function prototype out of a Lua 5.1 binary chunk.</summary>
public sealed class LuaProto
{
    public string? Source { get; init; }
    public int LineDefined { get; init; }
    public IReadOnlyList<uint> Code { get; init; } = [];
    public IReadOnlyList<object?> Constants { get; init; } = [];
    public IReadOnlyList<LuaProto> Protos { get; init; } = [];

    /// <summary>Name this function was assigned to, if it could be recovered from the enclosing chunk.</summary>
    public string? Name { get; internal set; }

    public string? ConstantString(int index) =>
        index >= 0 && index < Constants.Count ? Constants[index] as string : null;
}
