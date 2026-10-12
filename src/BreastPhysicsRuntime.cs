using Character;
using UnityEngine;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    // Read the character's Bust spring group, not skin-bone names. A skin/shape
    // bone below a physics root inherits its motion even when it is not itself
    // an explicitly simulated Transform (including nipple and overlay bones).
    private static HashSet<int> BreastPhysicsRoots(Human human)
    {
        var roots = new HashSet<int>();
        var springs = human?.Body?.magicaSpringSetting?.Get(HumanMagicaSpringSetting.Type.Bust);
        if (springs == null) return roots;
        foreach (var entry in springs)
        {
            var chain = entry?.Spring?.SerializeData?.rootBones;
            if (chain == null) continue;
            foreach (var root in chain)
                if (root != null) roots.Add(root.GetInstanceID());
        }
        return roots;
    }

    private static bool[] BreastPhysicsBones(Transform[] bones, HashSet<int> roots)
    {
        var result = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            for (var bone = bones[i]; bone != null; bone = bone.parent)
                if (roots.Contains(bone.GetInstanceID())) { result[i] = true; break; }
        return result;
    }

    private static string[] BreastPhysicsRootPaths(Human human)
    {
        var paths = new HashSet<string>();
        var springs = human?.Body?.magicaSpringSetting?.Get(HumanMagicaSpringSetting.Type.Bust);
        if (springs != null)
            foreach (var entry in springs)
            {
                var chain = entry?.Spring?.SerializeData?.rootBones;
                if (chain != null) foreach (var root in chain) if (root != null) paths.Add(PathOf(root));
            }
        return paths.ToArray();
    }

    private static float[] ComputeBreastWeights(Human human, Mesh mesh, int vertexCount, SkinnedMeshRenderer smr)
    {
        var weights = mesh.boneWeights;
        Transform[] bones = smr.bones;
        if (weights == null || weights.Length != vertexCount || bones == null) return null;
        var roots = BreastPhysicsRoots(human);
        var selected = BreastPhysicsBones(bones, roots);
        var result = new float[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var w = weights[i];
            float Sum(int index, float value) => (uint)index < selected.Length && selected[index] ? value : 0;
            result[i] = Mathf.Clamp01(Sum(w.boneIndex0, w.weight0) + Sum(w.boneIndex1, w.weight1)
                + Sum(w.boneIndex2, w.weight2) + Sum(w.boneIndex3, w.weight3));
        }
        RuntimeLogInfo($"[BreastPhysics] mesh={mesh.name} roots={roots.Count} skinBones={string.Join(",", bones.Where((b,i)=>selected[i]).Select(b=>b.name))} weightedVertices={result.Count(w=>w>0)}/{vertexCount}");
        return result;
    }
}
