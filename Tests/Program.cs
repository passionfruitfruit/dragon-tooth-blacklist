using DragonToothBlacklist.Core;
using System.Reflection.Emit;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}

string speed = Rules.Signature(new[] { new Affix("Speed", 12) });
string speedWeight = Rules.Signature(new[] { new Affix("Speed", 12), new Affix("Weight", 5) });
Check(speed != speedWeight, "Exact combination: speed-only does not match speed + weight");
Check(speedWeight == Rules.Signature(new[] { new Affix("Weight", 5), new Affix("Speed", 12) }), "Signature ignores affix order");
Check(speed != Rules.Signature(new[] { new Affix("Speed", 11) }), "Numeric outcomes retain level in their exact signature");
string speedKey = Rules.TypeSignature(new[] { new Affix("Speed", 12) });
string speedWeightKey = Rules.TypeSignature(new[] { new Affix("Speed", 12), new Affix("Weight", 5) });
Check(speedKey == Rules.TypeSignature(new[] { new Affix("Speed", 11) }), "Blacklist type combination ignores levels");
Check(speedWeightKey == Rules.TypeSignature(new[] { new Affix("Weight", 1), new Affix("Speed", 3), new Affix("Speed", 4) }),
    "Blacklist type combination ignores order and duplicate types");
var a = Rules.Read(null);
a.Add(speedKey);
string saved = Rules.Write(a);
var b = Rules.Read(null);
Check(b.Count == 0 && Rules.Read(saved).SetEquals(a), "Each weapon independently persists its blacklist");
var preview = Rules.Read(saved);
preview.Add(speedWeightKey);
Check(Rules.Read(saved).Count == 1, "Preview changes cannot modify live serialized weapon state");
var pool = new List<Outcome<string>>
{
    new(speed, .5, speed) { BlacklistKey = speedKey },
    new(speedWeight, .125, speedWeight) { BlacklistKey = speedWeightKey },
    new("Attack:1", .375, "Attack:1") { BlacklistKey = "Attack" }
};
Check(Rules.Pick(pool, a, .249999) == speedWeight && Rules.Pick(pool, a, .25) == "Attack:1", "Remaining 1:3 probability ratio is preserved");
Check(!Rules.ResetIfExhausted(a, pool) && a.Contains(speedKey), "Partial blacklist never resets");
a.UnionWith(pool.Select(o => o.BlacklistKey));
Check(Rules.ResetIfExhausted(a, pool) && a.Count == 0, "Exhausting all combinations resets blacklist");
Throws<InvalidOperationException>(() => Rules.ResetIfExhausted(a, Array.Empty<Outcome<string>>()), "Empty pool cannot erase blacklist");
Throws<System.Text.Json.JsonException>(() => Rules.Read("broken"), "Invalid save data is not silently erased");

string legacy = "[\"Attack:3\",\"Attack:5\",\"Speed:12|Weight:5\",\"Weight:2|Speed:11\"]";
var migrated = Rules.Read(legacy);
Check(migrated.SetEquals(new[] { "Attack", speedWeightKey }), "Legacy level variants migrate and deduplicate by complete type set");
Check(Rules.Read(Rules.Write(migrated)).SetEquals(migrated), "Migrated type-only save data round-trips");
migrated.Add(speedKey);
Check(Rules.Read(legacy).SetEquals(new[] { "Attack", speedWeightKey }), "Reading and previewing legacy state does not mutate stored source data");
Throws<InvalidDataException>(() => Rules.Read("[\"Attack:invalid\"]"), "Malformed legacy ranks are rejected without silently broadening blacklist");
Throws<InvalidDataException>(() => Rules.Read("[\"\"]"), "Empty legacy combinations are rejected");

string[] innateNames =
{
    "AttackEnchantment", "DefenseEnchantment", "CritEnchantment", "CritPowerEnchantment",
    "LightweightEnchantment", "SlimeGathererEnchantment", "SlimeSlayerEnchantment", "WeaponSpeedEnchantment"
};
foreach (string innate in innateNames)
{
    string fullType = "StardewValley.Enchantments." + innate;
    string partner = "StardewValley.Enchantments." + (innate == "AttackEnchantment" ? "DefenseEnchantment" : "AttackEnchantment");
    string one = Rules.TypeSignature(new[] { new Affix(fullType, 1) });
    string ranked = Rules.TypeSignature(new[] { new Affix(fullType, 5) });
    string pair = Rules.TypeSignature(new[] { new Affix(fullType, 3), new Affix(partner, 1) });
    string rankedPair = Rules.TypeSignature(new[] { new Affix(partner, 5), new Affix(fullType, 2) });
    var typePool = new List<Outcome<string>>
    {
        new(fullType + ":5", .6, "single") { BlacklistKey = ranked },
        new(fullType + ":2|" + partner + ":5", .4, "pair") { BlacklistKey = rankedPair }
    };
    Check(one == ranked && pair == rankedPair, $"{innate}: singleton and complete pair both ignore ranks");
    Check(Rules.Pick(typePool, new HashSet<string> { one }, 0) == "pair",
        $"{innate}: singleton ban permits a combination containing another type");
    Check(Rules.Pick(typePool, new HashSet<string> { pair }, .999) == "single",
        $"{innate}: pair ban permits singleton without the partner");
}

// Keep the native numeric templates separate before attaching their shared blacklist key.
var numericPool = BranchRandom.Explore(r => r.Next(8) switch
{
    0 or 1 => new[] { new Affix("Attack", 1) },
    2 => new[] { new Affix("Attack", 3) },
    3 or 4 or 5 => new[] { new Affix("Speed", 3) },
    6 => new[] { new Affix("Speed", 11) },
    _ => new[] { new Affix("Speed", 12), new Affix("Weight", 5) }
}, Rules.Signature, new[] { 0d, 1d }).Select(o => o with { BlacklistKey = Rules.TypeSignature(o.Value) }).ToList();
Check(numericPool.Count == 5 && numericPool.Select(o => o.BlacklistKey).Distinct().Count() == 3,
    "Numeric templates remain distinct while multiple ranks share a blacklist type group");
Check(Math.Abs(numericPool.Single(o => o.Signature == "Attack:1").Weight - .25) < 1e-12
    && Math.Abs(numericPool.Single(o => o.Signature == "Attack:3").Weight - .125) < 1e-12
    && Math.Abs(numericPool.Single(o => o.Signature == "Speed:3").Weight - .375) < 1e-12,
    "Grouping blacklist keys preserves original uneven numeric outcome probabilities");
var attackBan = new HashSet<string> { "Attack" };
Check(Enumerable.Range(0, 1000).All(i => !Rules.Pick(numericPool, attackBan, i / 1000d).Any(f => f.Type == "Attack")),
    "A type-group ban removes every attack rank from selection");
var rankedSelections = numericPool.OrderBy(o => o.Signature == "Speed:3" ? 0 : o.Signature == "Speed:11" ? 1 : 2).ToList();
Check(Rules.Signature(Rules.Pick(rankedSelections, attackBan, .6 - 1e-9)) == "Speed:3"
    && Rules.Signature(Rules.Pick(rankedSelections, attackBan, .6 + 1e-9)) == "Speed:11"
    && Rules.Signature(Rules.Pick(rankedSelections, attackBan, .8 - 1e-9)) == "Speed:11"
    && Rules.Signature(Rules.Pick(rankedSelections, attackBan, .8 + 1e-9)) == speedWeight,
    "Allowed numeric ranks retain their native 3:1 ratio after another type group is banned");
var bothSingletonBans = new HashSet<string> { "Attack", "Speed" };
Check(Enumerable.Range(0, 100).All(i => Rules.Signature(Rules.Pick(numericPool, bothSingletonBans, i / 100d)) == speedWeight),
    "Banning both singleton groups leaves the speed-plus-weight combination available");
Check(!Rules.ResetIfExhausted(bothSingletonBans, numericPool) && bothSingletonBans.Count == 2,
    "Exhaustion does not reset while a complete combination group remains available");
bothSingletonBans.Add(speedWeightKey);
Check(Rules.ResetIfExhausted(bothSingletonBans, numericPool) && bothSingletonBans.Count == 0,
    "Three banned type groups exhaust five numeric outcomes and clear the blacklist");

var branches = BranchRandom.Explore(r =>
{
    bool extra = r.NextDouble() < .125;
    int level = r.Next(1, 6);
    return $"{extra}:{level}";
}, s => s, new[] { 0d, .125, .5, 1d });
Check(branches.Count == 10 && Math.Abs(branches.Sum(o => o.Weight) - 1) < 1e-12, "All native random branches are enumerated");
Check(branches.Where(o => o.Value.StartsWith("True")).All(o => Math.Abs(o.Weight - .025) < 1e-12), "Rare 1/8 branch retains its exact weight");
Check(branches.Where(o => o.Value.StartsWith("False")).All(o => Math.Abs(o.Weight - .175) < 1e-12), "Common 7/8 branch retains its exact weight");
var collapsed = BranchRandom.Explore(r => Math.Min(2, r.Next(11) / 2 + 1).ToString(), s => s, new[] { 0d, 1d });
Check(collapsed.Count == 2 && Math.Abs(collapsed.Single(o => o.Value == "1").Weight - 2d / 11) < 1e-12, "Clamped levels aggregate probability instead of becoming uniform");
var nested = BranchRandom.Explore(r =>
    r.NextDouble() < .125 ? "defense" : r.NextDouble() < .125 ? "weight" : r.NextDouble() < .125 ? "slime" : "none",
    s => s, new[] { 0d, .125, .5, 1d });
Check(Math.Abs(nested.Single(o => o.Value == "weight").Weight - 7d / 64) < 1e-12,
    "Sequential optional branches preserve 7/64 weight probability");
Check(Math.Abs(nested.Single(o => o.Value == "none").Weight - 343d / 512) < 1e-12,
    "No-secondary branch preserves 343/512 probability");
var nestedBans = new HashSet<string> { "defense", "slime" };
var orderedNested = nested.OrderBy(o => o.Value == "weight" ? 0 : 1).ToList();
Check(Rules.Pick(orderedNested, nestedBans, 56d / 399 - 1e-9) == "weight"
    && Rules.Pick(orderedNested, nestedBans, 56d / 399 + 1e-9) == "none",
    "Conditional selection preserves sequential branch ratios after bans");
Throws<NotSupportedException>(() => BranchRandom.Explore(r => r.Next().ToString(), s => s, new[] { 0d, 1d }), "Unsupported random draw fails closed");
Throws<NotSupportedException>(() => BranchRandom.Explore(r => r.Next(1000).ToString(), s => s, new[] { 0d, 1d }), "Oversized random domain fails closed");
Throws<ArgumentOutOfRangeException>(() => Rules.Pick(pool, b, 1), "Invalid probability input is rejected");
Throws<InvalidOperationException>(() => Rules.Pick(pool, pool.Select(o => o.BlacklistKey).ToHashSet(), 0), "No illegal fallback combination when all blocked");
Check(RngGuard.IsThresholdComparison(OpCodes.Clt) && RngGuard.IsThresholdComparison(OpCodes.Clt_Un),
    "Vanilla Boolean comparison IL is accepted (coal-grey regression)");
Check(RngGuard.IsThresholdComparison(OpCodes.Bge_Un_S) && RngGuard.IsThresholdComparison(OpCodes.Blt),
    "Vanilla immediate branch comparisons remain accepted");
Check(!RngGuard.IsThresholdComparison(OpCodes.Add) && !RngGuard.IsThresholdComparison(OpCodes.Call)
    && !RngGuard.IsThresholdComparison(OpCodes.Brtrue) && !RngGuard.IsThresholdComparison(OpCodes.Beq),
    "Unsupported operations are still rejected by RNG guard");
// Inspect the actual compiler output, not a fabricated list of IL instructions.
var method = typeof(CompilerProbe).GetMethod(nameof(CompilerProbe.Draw))!;
var il = method.GetMethodBody()!.GetILAsByteArray()!;
bool verifiedCompilerComparison = false;
for (int i = 0; i + 15 < il.Length; i++)
{
    if (il[i] != 0x6f) continue; // callvirt
    var call = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
    if (call?.DeclaringType != typeof(Random) || call.Name != nameof(Random.NextDouble)) continue;
    Check(il[i + 5] == 0x23 && BitConverter.ToDouble(il, i + 6) == .5,
        "Compiler emits the expected floating threshold for vanilla-shaped condition");
    Check(il[i + 14] == 0xfe && il[i + 15] == 0x04 && RngGuard.IsThresholdComparison(OpCodes.Clt),
        "Actual Release compiler clt output passes the repaired guard");
    verifiedCompilerComparison = true;
    break;
}
Check(verifiedCompilerComparison, "Compiler regression fixture was found and inspected");
var directCuts = RngGuard.Analyze(typeof(CompilerProbe).GetMethod(nameof(CompilerProbe.Draw))!);
Check(directCuts.SequenceEqual(new[] { 0d, .5, 1d }), "Native thresholds are read without a fixed count");
var helperGenerator = typeof(StardewValley.Extensions.NativeShapedGenerator).GetMethod("Generate")!;
var helperCuts = RngGuard.Analyze(helperGenerator);
Check(helperCuts.SequenceEqual(new[] { 0d, .125, .25, .5, 1d }),
    "NextBool no-argument, double and float thresholds are verified from real compiled helper IL");
var helperPool = BranchRandom.Explore(r => StardewValley.Extensions.NativeShapedGenerator.Generate(r, true), s => s, helperCuts);
Check(helperPool.Count == 6 && Math.Abs(helperPool.Single(o => o.Value == "A:1").Weight - 1d/16) < 1e-12,
    "Helper-based generator is exhaustively explored with its original rare outcome weight");
Check(Math.Abs(helperPool.Single(o => o.Value == "B:2").Weight - 7d/64) < 1e-12,
    "Weighted float helper and integer-choice helper preserve conditional probability");
Throws<NotSupportedException>(() => RngGuard.Analyze(typeof(StardewValley.Extensions.NativeShapedGenerator).GetMethod("DynamicChance")!),
    "Unverifiable dynamic chance still fails closed");
Throws<NotSupportedException>(() => RngGuard.Analyze(typeof(StardewValley.Extensions.NativeShapedGenerator).GetMethod("ForeignHelper")!),
    "Unknown RNG helper still fails closed");
Console.WriteLine($"All {checks} checks passed.");

public static class CompilerProbe
{
    public static bool Draw(Random r, bool force, bool weapon) => (r.NextDouble() < .5 || force) && weapon;
}
