using System.Numerics;

namespace ALPregnancy;

// All matrix arithmetic stays in managed .NET. AL's generated Unity
// Matrix4x4.GetDeterminant wrapper contains invalid IL for a value-type this.
internal static class RestSpaceMapping
{
    internal static bool TryCreate(string[] sourceNames, Matrix4x4[] sourcePoses,
        string[] targetNames, Matrix4x4[] targetPoses, out Matrix4x4 map,
        out Matrix4x4 inverse, out int sharedBones, out float maxError)
    {
        map = inverse = Matrix4x4.Identity;
        sharedBones = 0;
        maxError = 0;
        if (sourceNames.Length != sourcePoses.Length || targetNames.Length != targetPoses.Length) return false;
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < targetNames.Length; i++)
            if (!string.IsNullOrEmpty(targetNames[i])) targets.TryAdd(targetNames[i], i);
        var matches = new List<(int Source, int Target)>();
        for (int i = 0; i < sourceNames.Length; i++)
            if (!string.IsNullOrEmpty(sourceNames[i]) && targets.TryGetValue(sourceNames[i], out int target)) matches.Add((i, target));
        sharedBones = matches.Count;
        if (sharedBones == 0) return false;
        var first = matches[0];
        if (!Matrix4x4.Invert(targetPoses[first.Target], out var targetInverse)) return false;
        // System.Numerics uses row vectors. Unity adapters transpose at the boundary.
        map = sourcePoses[first.Source] * targetInverse;
        if (!Finite(map) || !Matrix4x4.Invert(map, out inverse) || !Finite(inverse)) return false;
        float extent = 1;
        foreach (var pair in matches)
        {
            if (!Matrix4x4.Invert(sourcePoses[pair.Source], out var sourceRest) ||
                !Matrix4x4.Invert(targetPoses[pair.Target], out var targetRest)) return false;
            Vector3 source = Vector3.Transform(Vector3.Zero, sourceRest);
            Vector3 expected = Vector3.Transform(Vector3.Zero, targetRest);
            float error = Vector3.Distance(Vector3.Transform(source, map), expected);
            if (!float.IsFinite(error)) return false;
            maxError = MathF.Max(maxError, error);
            extent = MathF.Max(extent, expected.Length());
        }
        return maxError <= extent * 0.01f;
    }

    internal static bool TryCreateAliased(string[] sourceNames, Matrix4x4[] sourcePoses,
        string[] targetNames, Matrix4x4[] targetPoses, out Matrix4x4 map,
        out Matrix4x4 inverse, out int sharedBones, out float maxError)
    {
        map = inverse = Matrix4x4.Identity;
        sharedBones = 0; maxError = float.PositiveInfinity;
        if (sourceNames.Length != sourcePoses.Length || targetNames.Length != targetPoses.Length) return false;
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < targetNames.Length; i++)
            if (!string.IsNullOrEmpty(targetNames[i])) targets.TryAdd(targetNames[i], i);
        var source = new List<Vector3>();
        var target = new List<Vector3>();
        var used = new HashSet<int>();
        for (int i = 0; i < sourceNames.Length; i++)
        {
            string name = RigBoneNames.Canonical(sourceNames[i]);
            if (string.IsNullOrEmpty(name) || !targets.TryGetValue(name, out int j) || !used.Add(j)) continue;
            if (!Matrix4x4.Invert(sourcePoses[i], out var s) || !Matrix4x4.Invert(targetPoses[j], out var t)) return false;
            source.Add(s.Translation); target.Add(t.Translation);
        }
        sharedBones = source.Count;
        if (!RestPoseFit.TryFit(source.ToArray(), target.ToArray(), out map, out maxError) || !Finite(map)) return false;
        return Matrix4x4.Invert(map, out inverse) && Finite(inverse);
    }

    private static bool Finite(Matrix4x4 m)
        => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
           float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
           float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
           float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
