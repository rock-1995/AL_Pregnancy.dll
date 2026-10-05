namespace ALPregnancy;

// Optional native breast ownership exclusion; disabled in the 0.2.22 trial.
// UV duplicates share the exclusion; neighboring abdomen vertices do not.
internal static class BreastExclusion
{
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
