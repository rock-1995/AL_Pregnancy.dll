using System.Numerics;

namespace ALPregnancy;

// COM3D2's drape equations, expressed in AL's calibrated bind/reference space.
// No live T-pose assumption, vertex-number threshold, or native bone mutation.
internal static class SkirtSkinning
{
    internal readonly record struct Recipe(int Bone, float Alpha, float Free);
    internal sealed class Plan
    {
        internal bool[] Controlled, Released;
        internal Vector3[] Offsets, BindOffsets, Residual, Visual;
        internal float[][] Free;
    }

    // Confirmed in AL's native clothing palettes. Helpers and filename matches
    // alone are deliberately not enough to opt a garment into skirt handling.
    internal static bool IsBone(string name) => name != null && name.Length == 13 &&
        name.StartsWith("cf_j_sk_", StringComparison.Ordinal) && name[10] == '_' &&
        Digit(name[8]) && Digit(name[9]) && Digit(name[11]) && Digit(name[12]);

    private static bool Digit(char value) => value >= '0' && value <= '9';

    internal static Plan Prepare(Vector3[] original, Vector3[] deformed,
        VirtualWeights.Influence[][] weights, bool[] excluded, bool[] controlled,
        int[] parents, Vector3[] roots, int[] triangles, Matrix4x4 toReference,
        Matrix4x4 inverseFrame, TorsoProfile torso)
    {
        int count = original.Length, bones = controlled.Length;
        if (count != deformed.Length || count != weights.Length || !controlled.Any(x => x) ||
            !Matrix4x4.Invert(toReference, out var inverse)) return null;
        var points = new Vector3[count]; var reference = new Vector3[count];
        var deltas = new Vector3[count]; var magnitude = new float[count];
        var peak = new float[bones]; var sample = new Vector3[bones];
        var total = new float[bones]; var crest = new float[bones];
        bool Allowed(int i) => excluded == null || !excluded[i];
        var toTorso = toReference * inverseFrame;
        for (int i = 0; i < count; i++)
        {
            reference[i] = Vector3.Transform(original[i], toReference);
            points[i] = Vector3.Transform(original[i], toTorso);
            deltas[i] = Allowed(i) ? Vector3.TransformNormal(deformed[i] - original[i], toReference) : Vector3.Zero;
            magnitude[i] = deltas[i].Length();
            foreach (var w in weights[i])
                if (w.Weight > 0 && controlled[w.Bone]) peak[w.Bone] = MathF.Max(peak[w.Bone], magnitude[i]);
        }
        // Surface measure prevents dense tessellation from overpowering a sparse
        // patch of equal physical area. Computed once, never during pose updates.
        var area = SurfaceMeasure(reference, triangles);
        float tolerance = torso.Span * .001f;
        for (int i = 0; i < count; i++)
        {
            if (!Allowed(i)) continue;
            foreach (var w in weights[i])
            {
                int b = w.Bone;
                if (w.Weight <= 0 || !controlled[b] || peak[b] <= tolerance) continue;
                float mass = area[i] * w.Weight * BellyShape.Smooth((magnitude[i] / peak[b] - .75f) / .25f);
                sample[b] += deltas[i] * mass; total[b] += mass; crest[b] += points[i].Y * mass;
            }
        }
        for (int b = 0; b < bones; b++)
            if (total[b] > 0) { sample[b] /= total[b]; crest[b] /= total[b]; }

        // Cloth directly above a chain may belong to pelvis/spine rather than
        // the first skirt bone. Sample its angular column in AL torso space.
        var radial = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var p = points[i]; var r = new Vector3(p.X, 0, p.Z - torso.AxisAt(p.Y));
            if (r.LengthSquared() > 1e-10f) radial[i] = Vector3.Normalize(r);
        }
        for (int b = 0; b < bones; b++)
        {
            if (!controlled[b] || parents[b] >= 0) continue;
            var root = Vector3.Transform(roots[b], toTorso);
            var r = new Vector3(root.X, 0, root.Z - torso.AxisAt(root.Y));
            if (r.LengthSquared() < 1e-10f) continue;
            r = Vector3.Normalize(r);
            float Support(int i) => !Allowed(i) || points[i].Y < root.Y - torso.Span * .1f || points[i].Y > torso.Ribs
                ? 0 : BellyShape.Smooth((Vector3.Dot(radial[i], r) - .94f) / .06f);
            float high = 0;
            for (int i = 0; i < count; i++) if (Support(i) > .1f) high = MathF.Max(high, magnitude[i]);
            if (high <= sample[b].Length() + tolerance) continue;
            var sum = Vector3.Zero; float mass = 0, height = 0;
            for (int i = 0; i < count; i++)
            {
                float w = area[i] * Support(i) * BellyShape.Smooth((magnitude[i] / high - .85f) / .15f);
                sum += deltas[i] * w; height += points[i].Y * w; mass += w;
            }
            if (mass > 0) { sample[b] = sum / mass; crest[b] = height / mass; total[b] = mass; }
        }
        SkirtDrape.Resolve(parents, sample, controlled, tolerance, out var offsets, out var released);
        // A single long skin bone also needs intra-segment release below its crest.
        if (!total.Any(x => x > 0)) return null;
        var plan = new Plan { Controlled = controlled, Released = released, Offsets = offsets,
            BindOffsets = Array.ConvertAll(offsets, v => Vector3.TransformNormal(v, inverse)),
            Free = new float[count][], Residual = new Vector3[count], Visual = new Vector3[count] };
        var mixed = new bool[count]; var visual = new Vector3[count]; var carried = new Vector3[count];
        bool anyFree = false;
        for (int i = 0; i < count; i++)
        {
            var ws = weights[i]; plan.Free[i] = new float[ws.Length];
            float free = 0, ownership = 0; bool other = false;
            if (!Allowed(i)) continue;
            for (int k = 0; k < ws.Length; k++)
            {
                var w = ws[k]; if (w.Weight <= 0) continue;
                int b = w.Bone;
                if (!controlled[b]) { other = true; continue; }
                ownership += w.Weight;
                float share = released[b] ? 1 : total[b] > 0 ? BellyShape.Smooth((crest[b] - points[i].Y) / (torso.Span * .20f)) : 0;
                plan.Free[i][k] = share; free += w.Weight * share;
                carried[i] += offsets[b] * (w.Weight * share);
            }
            anyFree |= free > 0;
            visual[i] = ownership > 0 ? deltas[i] * (1 - Math.Clamp(free / ownership, 0, 1)) + carried[i] / ownership : deltas[i];
            mixed[i] = ownership > 0 && other;
        }
        if (!anyFree) return null;
        SkirtTransition.Solve(reference, deltas, visual, mixed, triangles, torso.Span);
        for (int i = 0; i < count; i++)
        {
            plan.Residual[i] = original[i] + Vector3.TransformNormal(visual[i] - carried[i], inverse);
            plan.Visual[i] = original[i] + Vector3.TransformNormal(visual[i], inverse);
        }
        return plan;
    }

    internal static float[] SurfaceMeasure(Vector3[] points, int[] triangles)
    {
        var area = new float[points.Length];
        for (int k = 0; k + 2 < triangles.Length; k += 3)
        {
            int a = triangles[k], b = triangles[k + 1], c = triangles[k + 2];
            float mass = Vector3.Cross(points[b] - points[a], points[c] - points[a]).Length() / 6;
            area[a] += mass; area[b] += mass; area[c] += mass;
        }
        // Degenerate/point-only meshes retain the original sampling convention.
        if (!area.Any(x => x > 0)) Array.Fill(area, 1f);
        return area;
    }

    internal static void Matrices(Matrix4x4 native, Vector3 offset, Vector3 bindOffset,
        Matrix4x4 virtualTransform, out Matrix4x4 supported, out Matrix4x4 free)
    {
        var carry = Vector3.TransformNormal(offset, virtualTransform);
        free = native; free.Translation += carry;
        supported = free; supported.Translation -= Vector3.TransformNormal(bindOffset, native);
    }

    internal static VirtualWeights.Influence[] Blend(VirtualWeights.Influence[] native,
        float alpha, float[] free, Func<Recipe, int> slot)
    {
        if (alpha < 0 || alpha > 1)
            return native.Select((w, k) => new VirtualWeights.Influence(w.Weight > 0 ? slot(new(w.Bone, alpha, free[k])) : w.Bone, w.Weight)).Where(w => w.Weight > 0).ToArray();
        var bones = new List<Recipe>(); var masses = new List<float>(); float virtualMass = 0;
        for (int k = 0; k < native.Length; k++)
        {
            var w = native[k]; if (w.Weight <= 0) continue;
            float f = free[k], upper = (1 - f) * (1 - alpha), mass = upper + f;
            virtualMass += w.Weight * (1 - f) * alpha;
            if (mass <= 0) continue;
            var recipe = new Recipe(w.Bone, 0, f / mass);
            int existing = bones.IndexOf(recipe);
            if (existing >= 0) masses[existing] += w.Weight * mass;
            else { bones.Add(recipe); masses.Add(w.Weight * mass); }
        }
        var result = new List<VirtualWeights.Influence>();
        int fuse = -1;
        if (virtualMass > 0 && bones.Count == 4)
        { fuse = 0; for (int k = 1; k < masses.Count; k++) if (masses[k] < masses[fuse]) fuse = k; }
        for (int k = 0; k < bones.Count; k++)
        {
            var r = bones[k]; float mass = masses[k];
            if (k == fuse)
            {
                float upperMass = mass * (1 - r.Free), sum = mass + virtualMass;
                r = new(r.Bone, virtualMass / (upperMass + virtualMass), mass * r.Free / sum); mass = sum;
            }
            result.Add(new(slot(r), mass));
        }
        if (virtualMass > 0 && fuse < 0) result.Add(new(slot(new(-1, 1, 0)), virtualMass));
        return result.ToArray();
    }
}
