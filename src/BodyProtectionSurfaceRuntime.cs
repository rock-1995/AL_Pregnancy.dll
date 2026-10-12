using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private static void ApplySharedBodyProtection(List<MeshRecord> records, LocalFrame frame, float stage)
    {
        var skin = records.Where(r => r.Renderer != null && r.Renderer.enabled && r.Renderer.gameObject.activeInHierarchy &&
            r.Renderer.sharedMesh == r.Mesh && BodyMeshSelection.IsBodyPiece(r.Renderer) && r.OrigVerts != null &&
            !(r.Mesh.name ?? "").Contains("shadow", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (skin.Length == 0) return;
        var frameMatrix = FrameMatrix(frame);
        var parts = skin.Select(r => BodyProtectionInput(r.OrigVerts, r.Mesh, r.Renderer.bones, r.Mesh.boneWeights,
            r.ToReference, frameMatrix, r.BreastExcluded, r.BreastWeights)).ToArray();
        var keeps = BodyProtection.BuildShared(parts, frame.Profile, BellyDeformSettings.Vtx);
        // Compute the full joined body before shading, clothing correspondence,
        // and virtual weights. Averaging positions later cannot repair a split
        // protection field: it would still assign different pose weights.
        for (int part = 0; part < skin.Length; part++)
        {
            var rec = skin[part]; rec.ProtectionKeep = keeps[part];
            if (!NMatrix.Invert(rec.ToReference, out var fromReference)) throw new InvalidOperationException("Invalid body protection mapping.");
            var deltaMap = frameMatrix * fromReference;
            var changed = (Vector3[])rec.OrigVerts.Clone();
            for (int i = 0; i < changed.Length; i++)
            {
                if (rec.ProtectionKeep[i] == 0) continue;
                var local = parts[part].Original[i];
                float influence = BellyShape.DeformationInfluence(rec.BellyInfluence == null ? 1 : rec.BellyInfluence[i], local, frame.Profile, BellyDeformSettings.Vtx);
                var delta = (BellyShape.Deform(local, frame.Profile, stage, BellyDeformSettings.Vtx) - local) * (influence * rec.ProtectionKeep[i]);
                var meshDelta = NVector.TransformNormal(delta, deltaMap);
                changed[i] += new Vector3(meshDelta.X, meshDelta.Y, meshDelta.Z);
            }
            rec.LastNewV = changed; rec.Mesh.vertices = changed; rec.AppliedSig = Sig(changed);
        }
    }
}
