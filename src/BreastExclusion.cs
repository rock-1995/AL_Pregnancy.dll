namespace ALPregnancy;

// Full-protection mask for the weighted breast guard. The historical binary
// builder remains available to replay old snapshots without changing them.
internal static class BreastExclusion
{
    internal static float RestoreAmount(float physicsWeight, float strength, bool enabled = true)
        => enabled ? Math.Clamp(Math.Clamp(physicsWeight, 0, 1) * MathF.Max(0, strength), 0, 1) : 0;
    internal static bool[] BuildFullyProtected(float[] weights, int count, int[] weldGroups = null, bool enabled = true, float strength = 1)
    {
        var mask = new bool[count];
        if (weights != null)
            for (int i = 0; i < Math.Min(count, weights.Length); i++)
                mask[i] = RestoreAmount(weights[i], strength, enabled) >= 1 - 1e-6f;
        ShareWelds(mask, weldGroups);
        return mask;
    }
    internal static bool[] Build(float[] weights, int count, int[] weldGroups = null, bool enabled = true)
    {
        var mask = new bool[count];
        if (!enabled) return mask;
        if (weights != null)
            for (int i = 0; i < Math.Min(count, weights.Length); i++) mask[i] = weights[i] > 0;
        ShareWelds(mask, weldGroups);
        return mask;
    }
    internal static void ShareWelds(bool[] mask, int[] groups)
    {
        if (mask == null || groups?.Length != mask.Length) return;
        var excluded = new HashSet<int>();
        for (int i = 0; i < mask.Length; i++) if (mask[i]) excluded.Add(groups[i]);
        for (int i = 0; i < mask.Length; i++) if (excluded.Contains(groups[i])) mask[i] = true;
    }
    internal static bool Contains(bool[] mask, int i) => mask != null && (uint)i < mask.Length && mask[i];
    internal static void Restore<T>(T[] original, T[] current, bool[] mask)
    {
        if (original == null || current == null || mask == null) return;
        int n = Math.Min(mask.Length, Math.Min(original.Length, current.Length));
        for (int i = 0; i < n; i++) if (mask[i]) current[i] = original[i];
    }
}
