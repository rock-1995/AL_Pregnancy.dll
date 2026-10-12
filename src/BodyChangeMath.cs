namespace ALPregnancy;

// Card/Unity independent. Values use the game's normalized slider units.
internal static class BodyChangeMath
{
    internal static double Progress(bool pregnant, double progress, int births, int x)
    {
        // Counts are nonnegative. This also gives x <= 0 a defined, division-free meaning.
        births = Math.Max(0, births);
        if (births >= x) return 1;
        return (births + (pregnant && double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0)) / x;
    }

    internal static float Add(float original, float delta, double progress, int lower, int upper)
    {
        // An unaffected channel must not be clamped just because another channel changes.
        if (delta == 0 || progress == 0) return original;
        double min = Math.Min(lower, upper) / 100d, max = Math.Max(lower, upper) / 100d;
        return (float)Math.Clamp(original + delta * progress, min, max);
    }

    internal static double ColorProgress(double progress, float maximum) =>
        Math.Min(progress, float.IsFinite(maximum) ? Math.Clamp(maximum, 0, 1) : .85);
}
