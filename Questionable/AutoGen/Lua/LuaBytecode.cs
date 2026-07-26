// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System.Text;

namespace Questionable.AutoGen.Lua;

/// <summary>
///     Minimal reader for the Lua 5.1 binary chunks (<c>.luab</c>) that ship in <c>game_script/</c>.
///     Only the parts needed to recover constants, call structure and nested function names are decoded;
///     this is not a decompiler.
/// </summary>
public static class LuaBytecode
{
    private static ReadOnlySpan<byte> Signature => "Lua"u8;

    public static LuaProto Parse(byte[] data)
    {
        if (data.Length < 12 || !data.AsSpan(0, 4).SequenceEqual(Signature))
            throw new InvalidDataException("Not a Lua binary chunk.");
        if (data[4] != 0x51)
            throw new InvalidDataException($"Unsupported Lua bytecode version 0x{data[4]:X2}, expected 0x51.");
        if (data[6] != 1)
            throw new InvalidDataException("Big-endian Lua chunks are not supported.");

        int sizeOfSizeT = data[8];
        if (sizeOfSizeT is not (4 or 8))
            throw new InvalidDataException($"Unsupported size_t width {sizeOfSizeT}.");

        int position = 12;
        LuaProto root = ReadFunction(data, ref position, sizeOfSizeT);
        AssignNames(root);
        return root;
    }

    private static LuaProto ReadFunction(byte[] d, ref int p, int sizeOfSizeT)
    {
        string? source = ReadString(d, ref p, sizeOfSizeT);
        int lineDefined = BitConverter.ToInt32(d, p);
        p += 4; // linedefined
        p += 4; // lastlinedefined
        p += 4; // nups, numparams, is_vararg, maxstacksize

        int codeCount = BitConverter.ToInt32(d, p);
        p += 4;
        uint[] code = new uint[codeCount];
        for (int i = 0; i < codeCount; i++)
        {
            code[i] = BitConverter.ToUInt32(d, p);
            p += 4;
        }

        int constantCount = BitConverter.ToInt32(d, p);
        p += 4;
        object?[] constants = new object?[constantCount];
        for (int i = 0; i < constantCount; i++)
        {
            byte type = d[p++];
            switch (type)
            {
                case 0:
                    constants[i] = null;
                    break;
                case 1:
                    constants[i] = d[p] != 0;
                    p += 1;
                    break;
                case 3:
                    constants[i] = BitConverter.ToDouble(d, p);
                    p += 8;
                    break;
                case 4:
                    constants[i] = ReadString(d, ref p, sizeOfSizeT);
                    break;
                default:
                    throw new InvalidDataException($"Unknown constant type {type}.");
            }
        }

        int protoCount = BitConverter.ToInt32(d, p);
        p += 4;
        LuaProto[] protos = new LuaProto[protoCount];
        for (int i = 0; i < protoCount; i++)
            protos[i] = ReadFunction(d, ref p, sizeOfSizeT);

        SkipDebugInfo(d, ref p, sizeOfSizeT);

        return new LuaProto
        {
            Source = source,
            LineDefined = lineDefined,
            Code = code,
            Constants = constants,
            Protos = protos
        };
    }

    private static void SkipDebugInfo(byte[] d, ref int p, int sizeOfSizeT)
    {
        int lineInfo = BitConverter.ToInt32(d, p);
        p += 4 + lineInfo * 4;

        int locVars = BitConverter.ToInt32(d, p);
        p += 4;
        for (int i = 0; i < locVars; i++)
        {
            ReadString(d, ref p, sizeOfSizeT);
            p += 8; // startpc, endpc
        }

        int upValues = BitConverter.ToInt32(d, p);
        p += 4;
        for (int i = 0; i < upValues; i++)
            ReadString(d, ref p, sizeOfSizeT);
    }

    private static string? ReadString(byte[] d, ref int p, int sizeOfSizeT)
    {
        ulong length = sizeOfSizeT == 4 ? BitConverter.ToUInt32(d, p) : BitConverter.ToUInt64(d, p);
        p += sizeOfSizeT;
        if (length == 0)
            return null;

        // The stored length includes the trailing NUL.
        string value = Encoding.UTF8.GetString(d, p, (int)length - 1);
        p += (int)length;
        return value;
    }

    /// <summary>
    ///     Recovers the name each nested function was stored under. Quest scripts declare everything as
    ///     <c>function Chunk.OnSceneNNNNN(...)</c>, which compiles to CLOSURE followed by SETTABLE/SETGLOBAL.
    /// </summary>
    private static void AssignNames(LuaProto proto)
    {
        int? pendingProto = null;
        foreach (uint instruction in proto.Code)
        {
            LuaOp op = LuaInstruction.Op(instruction);
            switch (op)
            {
                case LuaOp.Closure:
                    pendingProto = LuaInstruction.Bx(instruction);
                    break;

                case LuaOp.SetGlobal when pendingProto is { } index && index < proto.Protos.Count:
                    proto.Protos[index].Name ??= proto.ConstantString(LuaInstruction.Bx(instruction));
                    pendingProto = null;
                    break;

                case LuaOp.SetTable when pendingProto is { } index && index < proto.Protos.Count:
                    {
                        int b = LuaInstruction.B(instruction);
                        if (LuaInstruction.IsConstant(b))
                            proto.Protos[index].Name ??= proto.ConstantString(LuaInstruction.ConstantIndex(b));
                        pendingProto = null;
                        break;
                    }
            }
        }

        foreach (LuaProto nested in proto.Protos)
            AssignNames(nested);
    }
}
