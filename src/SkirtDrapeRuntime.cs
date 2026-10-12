using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private sealed class SkirtMesh
    {
        internal SkirtSkinning.Plan Plan;
        internal MeshRecord Record;
        internal Vector3[] DirectNormals;
        internal Vector4[] DirectTangents;
        internal NMatrix[] Supported, Free;
        internal int[] Ticks;
        internal SkirtSkinning.Recipe[] Recipes;
        internal bool Written;

        internal void Restore()
        {
            if (!Written || Record.Mesh == null) return;
            Record.Mesh.vertices = Record.LastNewV;
            Record.Mesh.normals = DirectNormals;
            Record.Mesh.tangents = DirectTangents;
            Record.AppliedSig = Sig(Record.LastNewV);
            Record.Mesh.RecalculateBounds();
            Written = false;
        }
    }

    private static VirtualWeights.Influence[] SkirtInfluences(BoneWeight w) => new[] {
        new VirtualWeights.Influence(w.boneIndex0, w.weight0), new VirtualWeights.Influence(w.boneIndex1, w.weight1),
        new VirtualWeights.Influence(w.boneIndex2, w.weight2), new VirtualWeights.Influence(w.boneIndex3, w.weight3) };

    private static SkirtMesh PrepareSkirt(MeshRecord record, VirtualBinding binding, CharaState state)
    {
        if (!record.IsCloth) return null;
        int count = binding.Bones.Length;
        var controlled = Array.ConvertAll(binding.Bones, b => b != null && SkirtSkinning.IsBone(b.name));
        if (!controlled.Any(x => x)) return null;
        var parents = new int[count]; Array.Fill(parents, -1);
        // IL2CPP wrappers may differ for the same native Transform. Use instance
        // identity, and follow the real hierarchy through omitted palette joints.
        var indices = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
            if (binding.Bones[i] != null) indices.TryAdd(binding.Bones[i].GetInstanceID(), i);
        for (int i = 0; i < count; i++)
        {
            if (!controlled[i]) continue;
            for (var p = binding.Bones[i].parent; p != null; p = p.parent)
                if (indices.TryGetValue(p.GetInstanceID(), out int j))
                { if (controlled[j]) parents[i] = j; break; }
        }
        var roots = new NVector[count];
        for (int i = 0; i < count; i++)
            if (controlled[i])
            {
                if (!NMatrix.Invert(MatrixBridge.ToManaged(binding.Binds[i]), out var rest))
                    throw new InvalidOperationException("Noninvertible skirt bind matrix.");
                roots[i] = rest.Translation;
            }
        if (!NMatrix.Invert(FrameMatrix(state.Frame), out var inverseFrame)) return null;
        var plan = SkirtSkinning.Prepare(ToManagedVectors(record.OrigVerts), ToManagedVectors(record.LastNewV),
            Array.ConvertAll(binding.Weights, SkirtInfluences), record.BreastExcluded,
            controlled, parents, roots, record.Mesh.triangles, record.ToReference, inverseFrame, state.Profile);
        if (plan == null) return null;
        return new SkirtMesh { Plan = plan, Record = record, Supported = new NMatrix[count], Free = new NMatrix[count], Ticks = new int[count] };
    }

    private static void InstallSkirtVertices(SkirtMesh skirt)
    {
        var r = skirt.Record;
        skirt.DirectNormals = r.Mesh.normals; skirt.DirectTangents = r.Mesh.tangents;
        // Set before any write so a partial failure also returns the direct shape.
        skirt.Written = true;
        var residual = ToUnityVectors(skirt.Plan.Residual);
        r.Mesh.vertices = residual;
        r.AppliedSig = Sig(residual);
        RecalculateNormalsWelded(r.Mesh, residual, r.NormalWeldGroup);
        try { r.Mesh.RecalculateTangents(); } catch { }
        RestoreExcludedBreastShading(r);
        r.Mesh.RecalculateBounds();
    }
}
