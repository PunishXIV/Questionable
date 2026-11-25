// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

namespace Questionable.AutoGen.Lua;

internal static class LuaInstruction
{
    private const int ConstantBit = 1 << 8;

    internal static LuaOp Op(uint i) => (LuaOp)(i & 0x3F);
    internal static int A(uint i) => (int)((i >> 6) & 0xFF);
    internal static int C(uint i) => (int)((i >> 14) & 0x1FF);
    internal static int B(uint i) => (int)((i >> 23) & 0x1FF);
    internal static int Bx(uint i) => (int)(i >> 14);

    internal static bool IsConstant(int rk) => (rk & ConstantBit) != 0;

    internal static int ConstantIndex(int rk) => rk & ~ConstantBit;
}
