using Character;
using UnityEngine;

namespace ALPregnancy;

internal static class BodyMeshSelection
{
    private static readonly HashSet<int> RegisteredBodyPieces = new();
    internal static void Clear() => RegisteredBodyPieces.Clear();
    internal static readonly string[] PelvisNames = { "cf_j_waist01", "cf_s_waist01", "cf_j_hips01" };
    internal static readonly string[] SpineNames = { "cf_j_spine01", "cf_s_spine01", "cf_j_spine02" };

    internal static bool IsBodyPiece(SkinnedMeshRenderer smr)
    {
        string rendererName = smr?.name ?? "";
        string meshName = smr?.sharedMesh?.name ?? "";
        return TorsoSelectionPolicy.IsBodyPiece(smr != null && RegisteredBodyPieces.Contains(smr.GetInstanceID()), rendererName, meshName);
    }

    internal static int FindBone(Transform[] bones, string[] names)
    {
        foreach (string name in names)
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == name) return i;
        return -1;
    }

    internal static SkinnedMeshRenderer Find(Human human)
    {
        Register(human);

        SkinnedMeshRenderer best = null;
        long bestScore = -1;
        foreach (var smr in human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null) continue;
            if (!IsBodyPiece(smr)) continue;
            Mesh mesh = smr.sharedMesh;
            Transform[] bones = smr.bones;
            int waist = -1, spine = -1, abdomen = 0;
            bool bindFrame = false;
            if (mesh.isReadable && bones != null)
            {
                Matrix4x4[] poses = mesh.bindposes;
                waist = FindBone(bones, PelvisNames);
                spine = FindBone(bones, SpineNames);
                if (poses != null && waist >= 0 && spine >= 0 && waist < poses.Length && spine < poses.Length)
                {
                    var delta = poses[spine].inverse.MultiplyPoint3x4(Vector3.zero) -
                                poses[waist].inverse.MultiplyPoint3x4(Vector3.zero);
                    bindFrame = float.IsFinite(delta.sqrMagnitude) && delta.sqrMagnitude > 1e-8f;
                }
                bool[] central = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    string name = bones[i]?.name?.ToLowerInvariant() ?? "";
                    central[i] = name.Contains("waist") || name.Contains("spine") || name.Contains("belly");
                }
                BoneWeight[] weights = mesh.boneWeights;
                if (weights != null)
                    foreach (var w in weights)
                    {
                        float sum = Contribution(w.boneIndex0, w.weight0, central) + Contribution(w.boneIndex1, w.weight1, central) +
                                    Contribution(w.boneIndex2, w.weight2, central) + Contribution(w.boneIndex3, w.weight3, central);
                        if (sum >= 0.15f) abdomen++;
                    }
            }
            bool active = smr.gameObject.activeInHierarchy && smr.enabled;
            long score = TorsoSelectionPolicy.Score(mesh.name, mesh.isReadable, MeshLease.Owns(mesh), bindFrame, abdomen, active);
            PregnancyPlugin.Logger.LogInfo($"BodyCandidate: renderer={smr.name}, mesh={mesh.name}, active={active}, readable={mesh.isReadable}, vertices={mesh.vertexCount}, waist={waist}, spine={spine}, abdomenVertices={abdomen}, score={score}");
            if (score > bestScore) { best = smr; bestScore = score; }
        }
        PregnancyPlugin.Logger.LogInfo("BodySelected: " + (best == null ? "NONE (no torso with valid rest-pose bones)" : best.sharedMesh.name + " bounds=" + best.sharedMesh.bounds));
        return best;
    }

    internal static void Register(Human human)
    {
        RegisteredBodyPieces.Clear();
        if (human?.Body?.BodyRenderers != null)
            foreach (var entry in human.Body.BodyRenderers)
                if (entry?.Renderer != null) RegisteredBodyPieces.Add(entry.Renderer.GetInstanceID());
    }

    private static float Contribution(int index, float weight, bool[] central)
        => index >= 0 && index < central.Length && central[index] ? weight : 0;
}
