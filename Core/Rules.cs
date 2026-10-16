using System.Text.Json;

namespace DragonToothBlacklist.Core;

public readonly record struct Affix(string Type, int Level);
public sealed record Outcome<T>(string Signature, double Weight, T Value)
{
    // Keep exact numeric outcomes separate even when their blacklist group is shared.
    public string BlacklistKey { get; init; } = Signature;
}

public static class Rules
{
    public static string Signature(IEnumerable<Affix> affixes) => string.Join("|", affixes
        .OrderBy(a => a.Type, StringComparer.Ordinal).ThenBy(a => a.Level)
        .Select(a => $"{a.Type}:{a.Level}"));

    public static string TypeSignature(IEnumerable<Affix> affixes) => string.Join("|", affixes
        .Select(a => a.Type).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal));

    private static string NormalizeStoredKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Empty blacklist combination.");
        var types = key.Split('|').Select(part =>
        {
            int colon = part.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(part[(colon + 1)..], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("Invalid legacy blacklist level.");
                part = part[..colon];
            }
            if (string.IsNullOrWhiteSpace(part)) throw new InvalidDataException("Empty blacklist affix type.");
            return new Affix(part, 0);
        });
        return TypeSignature(types);
    }

    public static HashSet<string> Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new(StringComparer.Ordinal);
        // Malformed data must not silently erase a player's blacklist.
        return new((JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidDataException("Invalid blacklist data.")).Select(NormalizeStoredKey), StringComparer.Ordinal);
    }

    public static string Write(IEnumerable<string> entries) => JsonSerializer.Serialize(entries.OrderBy(s => s, StringComparer.Ordinal));

    public static bool ResetIfExhausted<T>(HashSet<string> blocked, IEnumerable<Outcome<T>> pool)
    {
        var options = pool.ToArray();
        if (options.Length == 0) throw new InvalidOperationException("No vanilla outcomes.");
        if (options.Any(o => !blocked.Contains(o.BlacklistKey))) return false;
        blocked.Clear();
        return true;
    }

    public static T Pick<T>(IReadOnlyList<Outcome<T>> pool, HashSet<string> blocked, double roll)
    {
        if (roll < 0 || roll >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
        var allowed = pool.Where(o => !blocked.Contains(o.BlacklistKey)).ToArray();
        double total = allowed.Sum(o => o.Weight);
        if (!(total > 0)) throw new InvalidOperationException("All outcomes blocked.");
        double cursor = roll * total;
        foreach (var o in allowed)
        {
            cursor -= o.Weight;
            if (cursor < 0) return o.Value;
        }
        return allowed[^1].Value;
    }
}
