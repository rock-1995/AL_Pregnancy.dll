using System.Numerics;

namespace ALPregnancy;

// The collision mesh has its own topology and bone weights. Evaluate the same
// body shape and virtual attachment rules directly in that mesh's rest space.
// This object is built only on shape/topology changes; all arrays are low-poly.
internal sealed class CollisionDeformation
{
    internal Vector3[] Original, Static, Material, World, Support, CarrierLocal;
    internal float[] Blend;
    internal bool[] Candidates;
    internal int[] Indices;

    internal static CollisionDeformation Build(Vector3[] original, Matrix4x4 toReference, Matrix4x4 frame,
        TorsoProfile profile, float stage, VtxSettings settings, float[] influence, bool[] excluded,
        bool virtualActive, Func<Vector3, bool> supportContains)
    {
        if (!Matrix4x4.Invert(frame, out var inverseFrame) || !Matrix4x4.Invert(toReference, out var fromReference))
            throw new InvalidOperationException("Invalid low-poly rest frame.");
        int n = original.Length;
        var result = new CollisionDeformation { Original = original, Static = (Vector3[])original.Clone(), Material = new Vector3[n],
            World = new Vector3[n], Support = new Vector3[n], CarrierLocal = new Vector3[n], Blend = new float[n], Candidates = new bool[n] };
        for (int i = 0; i < n; i++)
        {
            var local = Vector3.Transform(original[i], toReference * inverseFrame);
            result.Material[i] = local / profile.Span;
            if (BreastExclusion.Contains(excluded, i)) continue;
            float effective = BellyShape.DeformationInfluence(influence[i], local.Y, profile, settings);
            if (effective > 0)
            {
                var delta = (BellyShape.Deform(local, profile, stage, settings) - local) * effective;
                result.Static[i] += Vector3.TransformNormal(delta, frame * fromReference);
            }
            result.Candidates[i] = virtualActive && supportContains?.Invoke(result.Material[i]) == true;
        }
        if (virtualActive)
        {
            for (int i = 0; i < n; i++)
                if (!BreastExclusion.Contains(excluded, i))
                    result.Blend[i] = VirtualAxisMath.MaterialSurfaceWeight(Vector3.TransformNormal(result.Static[i] - original[i], toReference).Length(),
                        result.Material[i] * profile.Span, profile, stage, settings, BellyShape.DeformationInfluence(influence[i], result.Material[i].Y * profile.Span, profile, settings));
        }
        result.Indices = Enumerable.Range(0, n).Where(i => !BreastExclusion.Contains(excluded, i) &&
            (Vector3.DistanceSquared(original[i], result.Static[i]) > 1e-14f || result.Blend[i] > 0 || result.Candidates[i])).ToArray();
        foreach (int i in result.Indices)
            if (!float.IsFinite(result.Static[i].LengthSquared()) || !float.IsFinite(result.Blend[i]))
                throw new InvalidOperationException("Nonfinite low-poly rest shape.");
        return result;
    }
}
