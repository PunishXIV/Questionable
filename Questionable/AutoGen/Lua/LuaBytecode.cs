// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System.Text;

namespace Questionable.AutoGen.Lua;

internal static class LuaBytecode
{
    private static ReadOnlySpan<byte> Signature => "Lua"u8;

    internal static LuaProto Parse(byte[] data)
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

    private static LuaProto ReadFunction(byte[] data, ref int position, int sizeOfSizeT)
    {
        string? source = ReadString(data, ref position, sizeOfSizeT);
        int lineDefined = BitConverter.ToInt32(data, position);
        position += 4; // linedefined
        position += 4; // lastlinedefined
        position += 4; // nups, numparams, is_vararg, maxstacksize

        int codeCount = BitConverter.ToInt32(data, position);
        position += 4;
        uint[] code = new uint[codeCount];
        for (int i = 0; i < codeCount; i++)
        {
            code[i] = BitConverter.ToUInt32(data, position);
            position += 4;
        }

        int constantCount = BitConverter.ToInt32(data, position);
        position += 4;
        object?[] constants = new object?[constantCount];
        for (int i = 0; i < constantCount; i++)
        {
            byte type = data[position++];
            switch (type)
            {
                case 0:
                    constants[i] = null;
                    break;
                case 1:
                    constants[i] = data[position] != 0;
                    position += 1;
                    break;
                case 3:
                    constants[i] = BitConverter.ToDouble(data, position);
                    position += 8;
                    break;
                case 4:
                    constants[i] = ReadString(data, ref position, sizeOfSizeT);
                    break;
                default:
                    throw new InvalidDataException($"Unknown constant type {type}.");
            }
        }

        int protoCount = BitConverter.ToInt32(data, position);
        position += 4;
        LuaProto[] protos = new LuaProto[protoCount];
        for (int i = 0; i < protoCount; i++)
            protos[i] = ReadFunction(data, ref position, sizeOfSizeT);

        SkipDebugInfo(data, ref position, sizeOfSizeT);

        return new LuaProto
        {
            Source = source,
            LineDefined = lineDefined,
            Code = code,
            Constants = constants,
            Protos = protos
        };
    }

    private static void SkipDebugInfo(byte[] data, ref int position, int sizeOfSizeT)
    {
        int lineInfo = BitConverter.ToInt32(data, position);
        position += 4 + lineInfo * 4;

        int locVars = BitConverter.ToInt32(data, position);
        position += 4;
        for (int i = 0; i < locVars; i++)
        {
            ReadString(data, ref position, sizeOfSizeT);
            position += 8; // startpc, endpc
        }

        int upValues = BitConverter.ToInt32(data, position);
        position += 4;
        for (int i = 0; i < upValues; i++)
            ReadString(data, ref position, sizeOfSizeT);
    }

    private static string? ReadString(byte[] data, ref int position, int sizeOfSizeT)
    {
        ulong length = sizeOfSizeT == 4 ? BitConverter.ToUInt32(data, position) : BitConverter.ToUInt64(data, position);
        position += sizeOfSizeT;
        if (length == 0)
            return null;

        // The stored length includes the trailing NUL.
        string value = Encoding.UTF8.GetString(data, position, (int)length - 1);
        position += (int)length;
        return value;
    }

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
