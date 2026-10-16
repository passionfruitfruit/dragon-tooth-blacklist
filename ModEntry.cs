using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DragonToothBlacklist.Core;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Menus;
using StardewValley.Tools;

namespace DragonToothBlacklist;

public sealed class ModEntry : Mod
{
    internal const string DataKey = "Luke.DragonToothBlacklist/Blacklist.v2";
    internal const string LegacyDataKey = "Luke.DragonToothBlacklist/Blacklist.v1";
    internal static ModEntry Instance = null!;
    private static readonly HashSet<Type> InnateTypes = new()
    {
        typeof(AttackEnchantment), typeof(DefenseEnchantment), typeof(CritEnchantment),
        typeof(CritPowerEnchantment), typeof(LightweightEnchantment), typeof(SlimeGathererEnchantment),
        typeof(SlimeSlayerEnchantment), typeof(WeaponSpeedEnchantment)
    };
    private static readonly Dictionary<string, List<Outcome<BaseEnchantment[]>>> Pools = new();
    private static bool generatorReady;
    private static double[] nativeCuts = new[] { 0d, 1d };
    private static string? generatorFailure;
    private static string? lastError;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        var harmony = new Harmony(ModManifest.UniqueID);
        string stage = "RNG analysis";
        try
        {
            var original = AccessTools.Method(typeof(MeleeWeapon), nameof(MeleeWeapon.attemptAddRandomInnateEnchantment));
            nativeCuts = RngGuard.Analyze(original);
            stage = "original generator reverse patch";
            harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(ModEntry), nameof(OriginalGenerate)))
                .Patch(HarmonyReversePatchType.Original);
            generatorReady = true;
            Monitor.Log("Verified native RNG thresholds: " + string.Join(", ", nativeCuts), LogLevel.Info);
        }
        catch (Exception ex)
        {
            generatorFailure = $"{stage}: {ex.GetBaseException().Message}";
            Monitor.Log($"Generator initialization failed during {stage}:\n{ex}", LogLevel.Error);
        }

        Patch(harmony, typeof(ForgeMenu), nameof(ForgeMenu.IsValidCraftIngredient), nameof(IngredientPrefix));
        Patch(harmony, typeof(ForgeMenu), nameof(ForgeMenu.HighlightItems), nameof(HighlightPrefix));
        Patch(harmony, typeof(ForgeMenu), nameof(ForgeMenu.IsValidCraft), nameof(ValidPrefix));
        Patch(harmony, typeof(ForgeMenu), nameof(ForgeMenu.GetForgeCost), nameof(CostPrefix));
        Patch(harmony, typeof(ForgeMenu), nameof(ForgeMenu.CraftItem), nameof(CraftPrefix));
        Patch(harmony, typeof(Tool), nameof(Tool.Forge), nameof(DragonPrefix));
        harmony.Patch(AccessTools.Method(typeof(ForgeMenu), "_UpdateDescriptionText"),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(DescriptionPostfix)));
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Pools.Clear();
        helper.ConsoleCommands.Add("dtb_check", "Report Dragon Tooth blacklist status for the held weapon.", (_, _) => CheckHeld());
        Monitor.Log("Loaded. Coal bans the full innate combination; quartz resets this weapon's blacklist.", LogLevel.Info);
    }

    private static void Patch(Harmony harmony, Type type, string method, string prefix) =>
        harmony.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(ModEntry), prefix));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Item OriginalGenerate(Item item, Random r, bool force, List<BaseEnchantment>? enchantsToReroll)
        => throw new NotSupportedException("Reverse patch was not installed.");

    private static bool IsInnate(BaseEnchantment enchantment) => InnateTypes.Contains(enchantment.GetType());
    private static BaseEnchantment[] Innates(MeleeWeapon weapon) => weapon.enchantments.Where(IsInnate).ToArray();
    private static string Signature(IEnumerable<BaseEnchantment> entries) => Rules.Signature(entries.Select(e => new Affix(e.GetType().FullName!, e.GetLevel())));
    private static string BlacklistSignature(IEnumerable<BaseEnchantment> entries) => Rules.TypeSignature(entries.Select(e => new Affix(e.GetType().FullName!, e.GetLevel())));
    private static HashSet<string> Read(MeleeWeapon weapon)
    {
        var blocked = Rules.Read(weapon.modData.TryGetValue(DataKey, out var json) ? json : null);
        blocked.UnionWith(Rules.Read(weapon.modData.TryGetValue(LegacyDataKey, out var legacy) ? legacy : null));
        return blocked;
    }
    private static void Write(MeleeWeapon weapon, HashSet<string> blocked)
    {
        if (blocked.Count == 0) weapon.modData.Remove(DataKey);
        else weapon.modData[DataKey] = Rules.Write(blocked);
        weapon.modData.Remove(LegacyDataKey);
    }
    private static void RemoveInnates(MeleeWeapon weapon)
    {
        foreach (var enchantment in Innates(weapon)) weapon.RemoveEnchantment(enchantment);
    }
    private static MeleeWeapon Clone(MeleeWeapon source)
    {
        var copy = (MeleeWeapon)source.getOne();
        // Explicitly preserve our state even if another mod customizes getOne.
        foreach (string key in new[] { DataKey, LegacyDataKey })
        {
            if (source.modData.TryGetValue(key, out string json)) copy.modData[key] = json;
            else copy.modData.Remove(key);
        }
        return copy;
    }

    private static List<Outcome<BaseEnchantment[]>> Pool(MeleeWeapon source)
    {
        if (!generatorReady) throw new NotSupportedException("Vanilla generator compatibility check failed: " + generatorFailure);
        var clean = Clone(source);
        RemoveInnates(clean);
        clean.modData.Remove(DataKey);
        clean.modData.Remove(LegacyDataKey);
        string key = $"{clean.QualifiedItemId}/{clean.getItemLevel()}/{clean.speed.Value}/{Signature(clean.enchantments)}";
        if (Pools.TryGetValue(key, out var pool)) return pool;
        pool = BranchRandom.Explore(random =>
        {
            var candidate = Clone(clean);
            OriginalGenerate(candidate, random, true, null);
            return Innates(candidate).Select(e => e.GetOne()).ToArray();
        }, Signature, nativeCuts).Select(o => o with { BlacklistKey = BlacklistSignature(o.Value) }).ToList();
        if (Pools.Count >= 64) Pools.Clear();
        Pools[key] = pool;
        return pool;
    }

    private static bool Custom(Item? item) => item?.QualifiedItemId is "(O)382" or "(O)80";
    private static bool Coal(Item? item) => item?.QualifiedItemId == "(O)382";
    private static bool ValidCustom(Item? left, Item? right)
    {
        if (left is not MeleeWeapon weapon || !Custom(right)) return false;
        try
        {
            var blocked = Read(weapon);
            if (!Coal(right)) return blocked.Count > 0;
            if (Innates(weapon).Length == 0) return false;
            _ = Pool(weapon);
            return true;
        }
        catch (Exception ex) { Report(ex); return false; }
    }

    private static bool IngredientPrefix(Item item, ref bool __result)
    {
        if (!Custom(item)) return true;
        __result = true;
        return false;
    }
    private static bool HighlightPrefix(ForgeMenu __instance, Item i, ref bool __result)
    {
        if (Custom(i))
        {
            __result = __instance.leftIngredientSpot.item == null || ValidCustom(__instance.leftIngredientSpot.item, i);
            return false;
        }
        if (Custom(__instance.rightIngredientSpot.item))
        {
            __result = ValidCustom(i, __instance.rightIngredientSpot.item);
            return false;
        }
        return true;
    }
    private static bool ValidPrefix(Item left_item, Item right_item, ref bool __result)
    {
        if (Custom(right_item)) { __result = ValidCustom(left_item, right_item); return false; }
        if (right_item?.QualifiedItemId == "(O)852" && left_item is MeleeWeapon weapon)
        {
            try { if (Read(weapon).Count > 0) _ = Pool(weapon); }
            catch (Exception ex) { Report(ex); __result = false; return false; }
        }
        return true;
    }
    private static bool CostPrefix(Item right_item, ref int __result)
    {
        if (!Custom(right_item)) return true;
        __result = 0;
        return false;
    }
    private static bool CraftPrefix(Item left_item, Item right_item, ref Item __result)
    {
        if (!Custom(right_item)) return true;
        if (!ValidCustom(left_item, right_item)) { __result = null!; return false; }
        var result = Clone((MeleeWeapon)left_item);
        var blocked = Read(result);
        if (Coal(right_item))
        {
            blocked.Add(BlacklistSignature(Innates(result)));
            Rules.ResetIfExhausted(blocked, Pool(result));
            RemoveInnates(result);
        }
        else blocked.Clear();
        Write(result, blocked);
        __result = result;
        return false;
    }

    // Restrict the hook to the actual Dragon Tooth material. Gem, shard, soul and
    // naturally dropped weapon generation continue through their original code.
    private static bool DragonPrefix(Tool __instance, Item item, bool count_towards_stats, ref bool __result)
    {
        if (item?.QualifiedItemId != "(O)852" || __instance is not MeleeWeapon weapon) return true;
        try
        {
            var blocked = Read(weapon);
            if (blocked.Count == 0) return true;
            if (!weapon.CanForge(item)) { __result = false; return false; }
            var pool = Pool(weapon);
            Rules.ResetIfExhausted(blocked, pool);
            var previousTypes = Innates(weapon).Select(e => e.GetType()).ToHashSet();
            var legalRerolls = pool.Where(o => !o.Value.Any(e => previousTypes.Contains(e.GetType()))).ToList();
            // Retain vanilla's previous-type avoidance whenever possible. If it
            // conflicts with every remaining allowed outcome, the exact blacklist
            // takes priority; only total exhaustion may reset it.
            if (!legalRerolls.Any(o => !blocked.Contains(o.BlacklistKey))) legalRerolls = pool;
            var chosen = Rules.Pick(legalRerolls, blocked, Game1.random.NextDouble());
            var trial = Clone(weapon);
            RemoveInnates(trial);
            foreach (var enchantment in chosen)
                if (!trial.AddEnchantment(enchantment.GetOne())) throw new InvalidOperationException("Could not apply a vanilla innate enchantment.");
            RemoveInnates(weapon);
            foreach (var enchantment in chosen)
                if (!weapon.AddEnchantment(enchantment.GetOne())) throw new InvalidOperationException("Could not apply a vanilla innate enchantment.");
            Write(weapon, blocked);
            __result = true;
            return false;
        }
        catch (Exception ex) { Report(ex); __result = false; return false; }
    }

    private static void DescriptionPostfix(ForgeMenu __instance)
    {
        Item right = __instance.rightIngredientSpot.item;
        if (!Custom(right) || __instance.IsBusy()) return;
        if (!ValidCustom(__instance.leftIngredientSpot.item, right)) return;
        // Keep vanilla inventory-space errors visible.
        if (!__instance.CanFitCraftedItem()) return;
        string text = Instance.Helper.Translation.Get(Coal(right) ? "forge.coal" : "forge.quartz");
        AccessTools.Field(typeof(ForgeMenu), "displayedDescription").SetValue(__instance, text);
    }
    private static void Report(Exception ex)
    {
        string error = ex.GetBaseException().Message;
        if (error == lastError) return;
        lastError = error;
        Instance.Monitor.Log($"Dragon Tooth blacklist operation disabled to preserve items: {error}", LogLevel.Error);
    }
    private void CheckHeld()
    {
        if (!Context.IsWorldReady || Game1.player.CurrentTool is not MeleeWeapon weapon)
        { Monitor.Log("Hold a melee weapon first.", LogLevel.Info); return; }
        try
        {
            var pool = Pool(weapon);
            var blocked = Read(weapon);
            Monitor.Log($"{weapon.DisplayName}: current={Signature(Innates(weapon))}; blocked type combinations={blocked.Count}; vanilla numeric outcomes={pool.Count}; vanilla type combinations={pool.Select(o => o.BlacklistKey).Distinct().Count()}; total probability={pool.Sum(o => o.Weight):R}", LogLevel.Info);
        }
        catch (Exception ex) { Report(ex); }
    }
}
