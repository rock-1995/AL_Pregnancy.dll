namespace ALPregnancy;

// Timing only: preserve the accepted geometric path and its full-term shape.
// Both the schedule and the shape's size interpolation are linear within each
// stage. Early waiting is controlled only by Start, not by hidden easing curves.
public static class PregnancyGrowthSchedule
{
    public readonly record struct Timing(double Start, double Early, double Middle, double Late);

    public static Timing Resolve(PregnancyRules rules)
    {
        static double Value(double value, double fallback) => double.IsFinite(value) ? value : fallback;
        double start = Math.Clamp(Value(rules.GrowthStart, 0), 0, .96);
        double early = Math.Clamp(Value(rules.GrowthEarlyEnd, .20), start + .01, .97);
        double middle = Math.Clamp(Value(rules.GrowthMiddleEnd, .45), early + .01, .98);
        double late = Math.Clamp(Value(rules.GrowthLateStart, .70), middle + .01, .99);
        return new(start, early, middle, late);
    }

    public static float ShapeStage(double progress, PregnancyRules rules)
    {
        if (!double.IsFinite(progress)) return 0;
        var t = Resolve(rules);
        if (progress <= t.Start) return 0;
        if (progress >= 1) return 1;
        // Keep these float anchors identical to BellyShape.Growth's shape keys.
        if (progress <= t.Early) return Interval(progress, t.Start, t.Early, 0, .33333334f);
        if (progress <= t.Middle) return Interval(progress, t.Early, t.Middle, .33333334f, .44444445f);
        if (progress <= t.Late) return Interval(progress, t.Middle, t.Late, .44444445f, .5555556f);
        return Interval(progress, t.Late, 1, .5555556f, 1);
    }

    private static float Interval(double progress, double start, double end, float shapeStart, float shapeEnd)
    {
        double fraction = Math.Clamp((progress - start) / (end - start), 0, 1);
        return (float)(shapeStart + (shapeEnd - shapeStart) * fraction);
    }
}
