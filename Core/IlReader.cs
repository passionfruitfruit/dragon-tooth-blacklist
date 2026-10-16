using System.Reflection;
using System.Reflection.Emit;

namespace DragonToothBlacklist.Core;

public readonly record struct IlInstruction(OpCode Opcode, object? Operand);

public static class IlReader
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    public static List<IlInstruction> Read(MethodInfo method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? throw new NotSupportedException("Method IL is unavailable: " + method);
        var result = new List<IlInstruction>();
        for (int p = 0; p < bytes.Length;)
        {
            short code = bytes[p++] == 0xfe ? unchecked((short)(0xfe00 | bytes[p++])) : bytes[p - 1];
            if (!Opcodes.TryGetValue(code, out var opcode)) throw new InvalidDataException("Unknown IL opcode.");
            object? operand = null;
            int size;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineI: operand = (sbyte)bytes[p]; size = 1; break;
                case OperandType.ShortInlineVar: operand = bytes[p]; size = 1; break;
                case OperandType.InlineVar: operand = BitConverter.ToUInt16(bytes, p); size = 2; break;
                case OperandType.ShortInlineR: operand = BitConverter.ToSingle(bytes, p); size = 4; break;
                case OperandType.InlineR: operand = BitConverter.ToDouble(bytes, p); size = 8; break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, p); size = 8; break;
                case OperandType.InlineMethod:
                    operand = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, p),
                        method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments());
                    size = 4; break;
                case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(bytes, p); break;
                case OperandType.ShortInlineBrTarget: size = 1; break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.InlineField:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType: size = 4; break;
                default: throw new NotSupportedException("Unsupported IL operand.");
            }
            if (p + size > bytes.Length) throw new InvalidDataException("Truncated IL.");
            p += size;
            if (opcode != OpCodes.Nop) result.Add(new(opcode, operand));
        }
        return result;
    }
}
