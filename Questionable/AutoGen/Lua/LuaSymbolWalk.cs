// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Lua;

/// <summary>
///     Walks a function body in program order and yields every string constant it touches
///     (globals it reads, table keys, comparison operands, call targets).
///     Quest scripts are generated from a template, so the <em>order</em> of these references is
///     meaningful: <c>SEQ_3</c> followed by <c>ACTOR3</c> means "at sequence 3, actor 3 is the target".
/// </summary>
public static class LuaSymbolWalk
{
    /// <summary>Referenced strings of <paramref name="proto"/> and everything nested inside it, in program order.</summary>
    public static IReadOnlyList<string> Deep(LuaProto proto)
    {
        List<string> result = [];
        Walk(proto, result);
        return result;
    }

    private static void Walk(LuaProto proto, List<string> result)
    {
        foreach (uint instruction in proto.Code)
        {
            switch (LuaInstruction.Op(instruction))
            {
                case LuaOp.LoadK:
                case LuaOp.GetGlobal:
                case LuaOp.SetGlobal:
                    Add(result, proto.ConstantString(LuaInstruction.Bx(instruction)));
                    break;

                case LuaOp.GetTable:
                case LuaOp.SetTable:
                case LuaOp.Self:
                case LuaOp.Eq:
                case LuaOp.Lt:
                case LuaOp.Le:
                case LuaOp.Add:
                case LuaOp.Sub:
                    AddRk(result, proto, LuaInstruction.B(instruction));
                    AddRk(result, proto, LuaInstruction.C(instruction));
                    break;

                case LuaOp.Closure:
                    {
                        int index = LuaInstruction.Bx(instruction);
                        if (index >= 0 && index < proto.Protos.Count)
                            Walk(proto.Protos[index], result);
                        break;
                    }
            }
        }
    }

    private static void AddRk(List<string> result, LuaProto proto, int rk)
    {
        if (LuaInstruction.IsConstant(rk))
            Add(result, proto.ConstantString(LuaInstruction.ConstantIndex(rk)));
    }

    private static void Add(List<string> result, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            result.Add(value);
    }
}
