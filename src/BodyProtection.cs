using System.Numerics;

namespace ALPregnancy;

// Rest/material-space protection. Rebuilt with the shape, never from a live pose.
internal static class BodyProtection
{
    internal sealed class Part
    {
        internal Vector3[] Original;
        internal int[] Triangles;
        internal bool[] BreastExcluded;
        internal float[] ThighWeights, PubicWeights, BreastWeights;
    }
    internal static float[][] BuildShared(IReadOnlyList<Part> parts, TorsoProfile torso, VtxSettings p)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        var excluded = new List<bool>(); var thigh = new List<float>(); var pubic = new List<float>(); var breast = new List<float>();
        bool hasBreastWeights = parts.Any(x => x.BreastWeights != null);
        int offset = 0;
        foreach (var part in parts)
        {
            vertices.AddRange(part.Original); excluded.AddRange(part.BreastExcluded);
            thigh.AddRange(part.ThighWeights); pubic.AddRange(part.PubicWeights);
            breast.AddRange(part.BreastWeights ?? new float[part.Original.Length]);
            foreach (int i in part.Triangles) triangles.Add(i + offset);
            offset += part.Original.Length;
        }
        var original = vertices.ToArray();
        float cell = torso.Span * .0003f;
        var groups = new Dictionary<(int, int, int), int>();
        var welds = original.Select((v, i) => {
            var key = ((int)MathF.Round(v.X / cell), (int)MathF.Round(v.Y / cell), (int)MathF.Round(v.Z / cell));
            if (!groups.TryGetValue(key, out int g)) groups[key] = g = i;
            return g;
        }).ToArray();
        var keep = Build(original, triangles.ToArray(), welds, excluded.ToArray(), thigh.ToArray(), pubic.ToArray(), torso, p,
            hasBreastWeights ? breast.ToArray() : null);
        var result = new float[parts.Count][]; offset = 0;
        for (int i = 0; i < parts.Count; i++) { result[i] = keep.AsSpan(offset, parts[i].Original.Length).ToArray(); offset += result[i].Length; }
        return result;
    }
    internal static float Keep(float[] keep, int i) => keep != null && (uint)i < keep.Length ? keep[i] : 1;
    internal static bool ThighBone(string name)
    {
        name = RigBoneNames.Canonical(name) ?? "";
        return name.StartsWith("cf_s_thigh", StringComparison.Ordinal) || name.StartsWith("cf_j_thigh", StringComparison.Ordinal)
            || name.StartsWith("cf_s_hipleg", StringComparison.Ordinal);
    }
    internal static bool PubicBone(string name) => (RigBoneNames.Canonical(name) ?? "").StartsWith("cf_s_kokan", StringComparison.Ordinal);
    private static float Smooth(float value) { float t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private static float Accelerate(float value, float speed) => 1 - MathF.Pow(1 - Math.Clamp(value, 0, 1), speed);

    // Same lower/side restore curve and 4*bone-weight gain as COM3D2's ALThighGuard.
    internal static float ThighRestore(Vector3 original, TorsoProfile torso, float thighWeight, VtxSettings p)
    {
        float materialY = HeightBoundary.Coordinate(original, torso, p);
        if (materialY >= torso.Navel) return 0;
        float lower = 0;
        if (p.ThighGuardSpeed > 0)
        {
            float epsilon = torso.Span * .0001f;
            float down = MathF.Max(torso.Navel - torso.PelvicFloor, epsilon);
            float sideRadius = MathF.Max(torso.WidthAt(torso.Navel), epsilon);
            float y = Accelerate(Smooth((torso.Navel - materialY) / down), p.ThighGuardSpeed);
            float side = Accelerate(Smooth((MathF.Abs(original.X) / sideRadius - .18f) / .62f), p.ThighGuardSpeed);
            float front = Smooth((original.Z - torso.BackAt(original.Y)) / MathF.Max(torso.FrontAt(original.Y) - torso.BackAt(original.Y), epsilon));
            lower = y * side * (1 - .65f * front);
        }
        float bone = Math.Clamp(thighWeight * 4 * MathF.Max(0, p.InnerThighGuardStrength), 0, 1);
        return 1 - (1 - lower) * (1 - bone);
    }
    internal static float BottomRestore(Vector3 original, TorsoProfile torso, float pubicWeight, VtxSettings p)
    {
        float materialY = HeightBoundary.Coordinate(original, torso, p);
        if (p.BottomGuardStrength <= 0 || materialY >= torso.Navel) return 0;
        // The bottom means the attachment to the pubis, not the whole lower half.
        // Leave the belly centre free. Pubic skin weights must never saturate the
        // old waist/(waist+leg) filter into full abdominal ownership.
        float width = MathF.Max(0, p.BottomGuardWidth) * torso.Span;
        float edge = width > 0 ? 1 - Smooth((materialY - torso.Pubis) / width) : 0;
        return Math.Clamp(MathF.Max(edge, Smooth(pubicWeight)) * p.BottomGuardStrength, 0, 1);
    }
    internal static float[] Build(Vector3[] original, int[] triangles, int[] welds, bool[] breastExcluded,
        float[] thighWeights, float[] pubicWeights, TorsoProfile torso, VtxSettings p, float[] breastWeights = null)
    {
        int n = original.Length;
        var keep = new float[n];
        var thighKeep = original.Select((v, i) => 1 - ThighRestore(v, torso, thighWeights?[i] ?? 0, p)).ToArray();
        SmoothProtectionBoundary(original, triangles, welds, thighKeep, torso.Span * p.ThighGuardWidth);
        float[] breastKeep = null;
        if (breastWeights != null)
        {
            breastKeep = Enumerable.Range(0, n).Select(i => 1 - BreastExclusion.RestoreAmount(breastWeights[i], p.BreastGuardStrength, p.BreastExclusionEnabled)).ToArray();
            // The seed itself carries its measured protection amount. A trace
            // weight is no longer a fully frozen boundary for the upper belly.
            SmoothProtectionBoundary(original, triangles, welds, breastKeep, torso.Span * p.LowerBreastGuardWidth,
                i => HeightBoundary.Coordinate(original[i], torso, p) > torso.Navel && original[i].Z > torso.AxisAt(original[i].Y));
        }
        // Compatibility for historical replays that only supply a binary mask.
        var breastDistance = breastKeep == null ? LowerBreastDistance(original, triangles, welds, breastExcluded, torso, p.LowerBreastGuardWidth * torso.Span) : null;
        for (int i = 0; i < n; i++)
        {
            float bottom = BottomRestore(original[i], torso, pubicWeights?[i] ?? 0, p);
            float breast = breastKeep != null ? breastKeep[i] : breastDistance == null ? 1 : Smooth(breastDistance[i] / (p.LowerBreastGuardWidth * torso.Span));
            keep[i] = BreastExclusion.Contains(breastExcluded, i) ? 0 : thighKeep[i] * (1 - bottom) * breast;
        }
        // UV split copies are the same physical point, not separate boundaries.
        if (welds?.Length == n)
        {
            var minima = new Dictionary<int, float>();
            for (int i = 0; i < n; i++) minima[welds[i]] = minima.TryGetValue(welds[i], out float v) ? MathF.Min(v, keep[i]) : keep[i];
            for (int i = 0; i < n; i++) keep[i] = minima[welds[i]];
        }
        return keep;
    }
    private static List<int>[] Neighbors(Vector3[] v, int[] triangles, int[] welds)
    {
        int n = v.Length;
        var neighbors = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        void Edge(int a, int b) { if ((uint)a < n && (uint)b < n) { neighbors[a].Add(b); neighbors[b].Add(a); } }
        if (triangles != null) for (int i = 0; i + 2 < triangles.Length; i += 3)
        { Edge(triangles[i], triangles[i + 1]); Edge(triangles[i + 1], triangles[i + 2]); Edge(triangles[i + 2], triangles[i]); }
        if (welds?.Length == n)
        {
            var first = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) if (first.TryGetValue(welds[i], out int j)) Edge(i, j); else first[welds[i]] = i;
        }
        return neighbors;
    }
    private static void SmoothProtectionBoundary(Vector3[] v, int[] triangles, int[] welds, float[] keep, float width, Func<int, bool> eligible = null)
    {
        if (width <= 0 || !keep.Any(x => x < 1)) return;
        var neighbors = Neighbors(v, triangles, welds);
        var distance = Enumerable.Repeat(width, v.Length).ToArray();
        var queue = new PriorityQueue<int, float>();
        // Interpret the existing restore weights as positions on a smooth
        // transition, then extend the protection over a physical surface length.
        // This bounds the spatial slope without diluting fully protected skin.
        for (int i = 0; i < keep.Length; i++) if (keep[i] < 1)
        {
            float lo = 0, hi = 1;
            for (int step = 0; step < 22; step++) { float mid = (lo + hi) * .5f; if (Smooth(mid) < keep[i]) lo = mid; else hi = mid; }
            distance[i] = keep[i] == 0 ? 0 : (lo + hi) * .5f * width;
            queue.Enqueue(i, distance[i]);
        }
        while (queue.TryDequeue(out int i, out float d))
        {
            if (d != distance[i]) continue;
            foreach (int j in neighbors[i])
            {
                if (eligible != null && !eligible(j)) continue;
                float next = d + Vector3.Distance(v[i], v[j]);
                if (next >= distance[j]) continue;
                distance[j] = next; queue.Enqueue(j, next);
            }
        }
        for (int i = 0; i < keep.Length; i++) keep[i] = MathF.Min(keep[i], Smooth(distance[i] / width));
    }
    private static float[] LowerBreastDistance(Vector3[] v, int[] triangles, int[] welds, bool[] excluded, TorsoProfile torso, float width)
    {
        if (width <= 0 || excluded == null || !excluded.Any(x => x) || triangles == null) return null;
        int n = v.Length;
        var neighbors = Neighbors(v, triangles, welds);
        var distance = Enumerable.Repeat(width, n).ToArray();
        var queue = new PriorityQueue<int, float>();
        bool Eligible(int i) => !BreastExclusion.Contains(excluded, i) && v[i].Y > torso.Navel && v[i].Z > torso.AxisAt(v[i].Y);
        void Offer(int i, float d) { if (d < distance[i]) { distance[i] = d; queue.Enqueue(i, d); } }
        // Seed only the lower edge of the actual physics-weighted surface.
        // Sternum names, bone origins, and pose-dependent distances play no role.
        for (int i = 0; i < n; i++) if (BreastExclusion.Contains(excluded, i))
            foreach (int j in neighbors[i]) if (Eligible(j) && v[j].Y <= v[i].Y)
                Offer(j, Vector3.Distance(v[i], v[j]));
        while (queue.TryDequeue(out int i, out float d))
        {
            if (d != distance[i]) continue;
            foreach (int j in neighbors[i]) if (Eligible(j)) Offer(j, d + Vector3.Distance(v[i], v[j]));
        }
        return distance;
    }
}
