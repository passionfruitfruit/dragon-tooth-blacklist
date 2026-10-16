namespace DragonToothBlacklist.Core;

public sealed class NeedBranch : Exception
{
    public double[] Values { get; }
    public double[] Weights { get; }
    public NeedBranch(double[] values, double[] weights) { Values = values; Weights = weights; }
}

/// <summary>Enumerates each bounded RNG branch, multiplying its original probability.</summary>
public sealed class BranchRandom : Random
{
    private readonly IReadOnlyList<double> script;
    private readonly double[] cuts;
    private int position;
    public BranchRandom(IReadOnlyList<double> script, double[] cuts) { this.script = script; this.cuts = cuts; }
    private double Draw(double[] values, double[] weights)
    {
        if (position >= 32) throw new NotSupportedException("Random branch depth exceeds safety limit.");
        if (position == script.Count) throw new NeedBranch(values, weights);
        double value = script[position++];
        if (!values.Contains(value)) throw new InvalidOperationException("Inconsistent RNG branch.");
        return value;
    }
    public override int Next(int maxValue) => Next(0, maxValue);
    public override int Next(int minValue, int maxValue)
    {
        int n = maxValue - minValue;
        if (n < 0 || n > 256) throw new NotSupportedException("Unsupported random domain.");
        if (n <= 1) return minValue;
        return (int)Draw(Enumerable.Range(minValue, n).Select(i => (double)i).ToArray(), Enumerable.Repeat(1d / n, n).ToArray());
    }
    public override int Next() => throw new NotSupportedException("Unbounded random draw.");
    public override double NextDouble()
    {
        var values = new double[cuts.Length - 1];
        var weights = new double[values.Length];
        for (int i = 0; i < values.Length; i++) { values[i] = (cuts[i] + cuts[i + 1]) / 2; weights[i] = cuts[i + 1] - cuts[i]; }
        return Draw(values, weights);
    }
    public override void NextBytes(byte[] buffer) => throw new NotSupportedException("Byte random draw.");
    public static List<Outcome<T>> Explore<T>(Func<Random, T> generate, Func<T, string> signature, double[] cuts)
    {
        var pool = new Dictionary<string, Outcome<T>>(StringComparer.Ordinal);
        int visited = 0;
        void Visit(List<double> path, double weight)
        {
            if (++visited > 100000) throw new NotSupportedException("Too many vanilla random branches.");
            try
            {
                T result = generate(new BranchRandom(path, cuts));
                string key = signature(result);
                if (key.Length == 0) throw new InvalidOperationException("Vanilla generated no innate enchantment.");
                pool[key] = pool.TryGetValue(key, out var existing)
                    ? existing with { Weight = existing.Weight + weight } : new(key, weight, result);
            }
            catch (NeedBranch branch)
            {
                for (int i = 0; i < branch.Values.Length; i++)
                {
                    path.Add(branch.Values[i]);
                    Visit(path, weight * branch.Weights[i]);
                    path.RemoveAt(path.Count - 1);
                }
            }
        }
        Visit(new(), 1);
        if (Math.Abs(pool.Values.Sum(o => o.Weight) - 1) > 1e-8) throw new InvalidOperationException("Incomplete probability tree.");
        return pool.Values.OrderBy(o => o.Signature, StringComparer.Ordinal).ToList();
    }
}
