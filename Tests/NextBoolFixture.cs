// Test-only equivalents of the game's public RNG helper contracts.
namespace StardewValley.Extensions;

public static class RandomExtensions
{
    public static bool NextBool(this Random random) => random.NextDouble() < .5;
    public static bool NextBool(this Random random, double chance) => chance >= 1 || random.NextDouble() < chance;
    public static bool NextBool(this Random random, float chance) => chance >= 1 || random.NextDouble() < chance;
    public static T Choose<T>(this Random random, T a, T b) => random.Next(2) == 0 ? a : b;
}

public static class NativeShapedGenerator
{
    public static string Generate(Random r, bool force)
    {
        if (!r.NextBool() && !force) return "absent";
        string optional = r.NextBool(.125d) ? "A" : r.NextBool(.25f) ? "B" : "none";
        return optional + ":" + r.Choose(1, 2);
    }
    public static bool DynamicChance(Random r, double chance) => r.NextBool(chance);
    public static bool ForeignHelper(Random r) => OtherRandom.Bias(r);
}
public static class OtherRandom { public static bool Bias(Random r) => r.NextDouble() < .7; }
