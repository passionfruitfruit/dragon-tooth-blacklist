using System.Reflection.Emit;
using System.Reflection;

namespace DragonToothBlacklist.Core;

public static class RngGuard
{
    // A C# comparison may either branch immediately or push a Boolean with clt.
    // In particular, "random < .5 || force" uses clt + or in Release builds.
    public static bool IsThresholdComparison(OpCode opcode) => opcode == OpCodes.Clt || opcode == OpCodes.Clt_Un
        || opcode == OpCodes.Bge || opcode == OpCodes.Bge_S || opcode == OpCodes.Bge_Un || opcode == OpCodes.Bge_Un_S
        || opcode == OpCodes.Blt || opcode == OpCodes.Blt_S || opcode == OpCodes.Blt_Un || opcode == OpCodes.Blt_Un_S;

    public static double[] Analyze(MethodInfo generator)
    {
        var instructions = IlReader.Read(generator);
        var thresholds = new List<double>();
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].Operand is not MethodInfo call) continue;
            if (call.DeclaringType == typeof(Random))
            {
                if (call.Name == nameof(Random.NextDouble))
                {
                    if (i + 2 >= instructions.Count || !TryConstant(instructions[i + 1], out double threshold)
                        || !IsThresholdComparison(instructions[i + 2].Opcode))
                        throw new NotSupportedException("Unknown direct floating RNG comparison: " + call);
                    thresholds.Add(threshold);
                }
                else if (call.Name != nameof(Random.Next) || call.GetParameters().Length is < 1 or > 2)
                    throw new NotSupportedException("Unknown RNG call: " + call);
            }
            else if (call.GetParameters().Any(p => p.ParameterType == typeof(Random)))
            {
                if (!call.IsStatic || call.DeclaringType?.FullName != "StardewValley.Extensions.RandomExtensions")
                    throw new NotSupportedException("Unknown RNG helper: " + call);
                if (call.Name == "NextBool") thresholds.Add(AnalyzeNextBool(call, i > 0 ? instructions[i - 1] : default));
                else if (call.Name is "Choose" or "ChooseFrom") ValidateIntegerChoice(call);
                else throw new NotSupportedException("Unknown RNG helper: " + call);
            }
        }
        if (thresholds.Count == 0) throw new NotSupportedException("No verifiable floating RNG decisions in native generator.");
        if (thresholds.Any(t => double.IsNaN(t) || double.IsInfinity(t) || t < 0 || t > 1))
            throw new NotSupportedException("Invalid native probability threshold.");
        return thresholds.Append(0).Append(1).Distinct().OrderBy(t => t).ToArray();
    }

    private static void ValidateIntegerChoice(MethodInfo helper)
    {
        var calls = IlReader.Read(helper).Where(i => i.Operand is MethodInfo).Select(i => (MethodInfo)i.Operand!).ToArray();
        var randomCalls = calls.Where(m => m.DeclaringType == typeof(Random) || m.GetParameters().Any(p => p.ParameterType == typeof(Random))).ToArray();
        if (randomCalls.Length != 1 || randomCalls[0].DeclaringType != typeof(Random)
            || randomCalls[0].Name != nameof(Random.Next) || randomCalls[0].GetParameters().Length is < 1 or > 2)
            throw new NotSupportedException("Unknown integer-choice RNG implementation: " + helper);
    }

    private static double AnalyzeNextBool(MethodInfo helper, IlInstruction previous)
    {
        var parameters = helper.GetParameters();
        if (helper.ReturnType != typeof(bool) || parameters.Length is < 1 or > 2 || parameters[0].ParameterType != typeof(Random))
            throw new NotSupportedException("Unknown NextBool signature.");
        var body = IlReader.Read(helper);
        int draw = body.FindIndex(i => i.Operand is MethodInfo m && m.DeclaringType == typeof(Random) && m.Name == nameof(Random.NextDouble));
        if (draw < 0 || body.Count(i => i.Operand is MethodInfo) != 1)
            throw new NotSupportedException("Unknown NextBool implementation.");
        if (parameters.Length == 1)
        {
            if (draw + 2 >= body.Count || !TryConstant(body[draw + 1], out double threshold)
                || !IsThresholdComparison(body[draw + 2].Opcode))
                throw new NotSupportedException("Unknown unweighted NextBool comparison.");
            return threshold;
        }
        if (parameters[1].ParameterType != typeof(double) && parameters[1].ParameterType != typeof(float))
            throw new NotSupportedException("Unknown NextBool probability type.");
        if (!TryConstant(previous, out double chance)) throw new NotSupportedException("NextBool probability is not a constant.");
        int comparison = draw + 2;
        if (draw + 1 >= body.Count || body[draw + 1].Opcode != OpCodes.Ldarg_1)
            throw new NotSupportedException("NextBool does not compare against its probability argument.");
        if (comparison < body.Count && body[comparison].Opcode == OpCodes.Conv_R8) comparison++;
        if (comparison >= body.Count || !IsThresholdComparison(body[comparison].Opcode))
            throw new NotSupportedException("Unknown weighted NextBool comparison.");
        return chance;
    }
    private static bool TryConstant(IlInstruction instruction, out double value)
    {
        if (instruction.Opcode == OpCodes.Ldc_R8 && instruction.Operand is double d) { value = d; return true; }
        if (instruction.Opcode == OpCodes.Ldc_R4 && instruction.Operand is float f) { value = f; return true; }
        value = 0;
        return false;
    }
}
