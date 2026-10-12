using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private static float[] BuildBodyProtection(Vector3[] original, Mesh mesh, Transform[] bones, BoneWeight[] weights,
        NMatrix toReference, NMatrix frame, TorsoProfile profile, int[] welds, bool[] excluded, float[] breastWeights = null)
    {
        var part = BodyProtectionInput(original, mesh, bones, weights, toReference, frame, excluded, breastWeights);
        return BodyProtection.Build(part.Original, part.Triangles, welds, excluded, part.ThighWeights, part.PubicWeights, profile, BellyDeformSettings.Vtx, breastWeights);
    }
    private static BodyProtection.Part BodyProtectionInput(Vector3[] original, Mesh mesh, Transform[] bones, BoneWeight[] weights,
        NMatrix toReference, NMatrix frame, bool[] excluded, float[] breastWeights = null)
    {
        NMatrix.Invert(frame, out var inverseFrame);
        var toTorso = toReference * inverseFrame;
        var material = ToManagedVectors(original).Select(v => NVector.Transform(v, toTorso)).ToArray();
        var thighBones = bones.Select(b => b != null && BodyProtection.ThighBone(b.name)).ToArray();
        var pubicBones = bones.Select(b => b != null && BodyProtection.PubicBone(b.name)).ToArray();
        var thigh = new float[original.Length]; var pubic = new float[original.Length];
        for (int i = 0; i < original.Length && i < weights.Length; i++)
        {
            var w = weights[i];
            void Add(int index, float weight)
            {
                if ((uint)index >= bones.Length || weight <= 0) return;
                if (thighBones[index]) thigh[i] += weight;
                if (pubicBones[index]) pubic[i] += weight;
            }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        }
        return new BodyProtection.Part { Original = material, Triangles = mesh.triangles,
            BreastExcluded = excluded, ThighWeights = thigh, PubicWeights = pubic, BreastWeights = breastWeights };
    }
}
