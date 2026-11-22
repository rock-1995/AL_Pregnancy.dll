using Character;
using System.Diagnostics;
using UnityEngine;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    // The caller registers the current actor before classifying a whole scan.
    // Unknown/overlay meshes remain on the conservative body invalidation path.
    internal static bool IsClothingRenderer(SkinnedMeshRenderer renderer)
        => IsBodyMorphClothSMR(renderer, null, requireReadable: false);

    internal static void BeforeClothingMeshesChanged(int charaId)
    {
        foreach (var key in KeysForChara(charaId))
            if (_records.TryGetValue(key, out var records))
                for (int i = records.Count - 1; i >= 0; --i)
                {
                    var record = records[i];
                    if (!record.IsCloth || (record.Renderer != null && record.Renderer.sharedMesh == record.Mesh)) continue;
                    UndoRecord(record);
                    records.RemoveAt(i);
                }
    }

    internal static void InvalidateClothingMeshes(int charaId)
    {
        foreach (var key in KeysForChara(charaId))
            if (_state.TryGetValue(key, out var state))
            {
                state.ClothEntries = null;
                state.LastClothApplied = -1;
                state.VisibilityDirty = true;
            }
    }

    private static VirtualBinding NativeBinding(SkinnedMeshRenderer renderer)
    {
        if (renderer == null) return null;
        foreach (var records in _records.Values)
            foreach (var record in records)
                if (record.Virtual != null && record.Renderer == renderer && renderer.sharedMesh == record.Mesh)
                    return record.Virtual;
        return null;
    }

    private static void BeginRebuild(CharaState state, string mode)
    {
        state.RebuildStarted = Stopwatch.GetTimestamp();
        state.RebuildMode = mode;
        state.RecomputedClothes = state.ReusedClothes = 0;
        state.RebuiltBodySurface = false;
    }

    private static void FinishRebuild(CharaState state)
    {
        if (state.RebuildStarted == 0) return;
        double ms = (Stopwatch.GetTimestamp() - state.RebuildStarted) * 1000d / Stopwatch.Frequency;
        Log.LogInfo($"[Morph rebuild] actor={state.CharaId} mode={state.RebuildMode} geometry+bindingMs={ms:F2} bodySurface={state.RebuiltBodySurface} clothesComputed={state.RecomputedClothes} clothesReused={state.ReusedClothes}");
        state.RebuildStarted = 0;
    }

    private static HashSet<MeshRecord> ActiveBodyRecords(List<MeshRecord> records)
    {
        var result = new HashSet<MeshRecord>();
        foreach (var record in records)
            if (record.Renderer != null && record.Renderer.enabled && record.Renderer.gameObject.activeInHierarchy &&
                record.Renderer.sharedMesh == record.Mesh && BodyMeshSelection.IsBodyPiece(record.Renderer))
                result.Add(record);
        return result;
    }

    private static BodyAnchorContext BuildBodyAnchor(List<MeshRecord> records, LocalFrame frame)
    {
        var anchor = CreateBodyAnchorContext(frame);
        foreach (var record in records)
            if (record.Renderer != null && BodyMeshSelection.IsBodyPiece(record.Renderer) &&
                record.Renderer.enabled && record.Renderer.gameObject.activeInHierarchy && record.LastNewV != null &&
                !(record.Mesh.name ?? "").Contains("shadow", StringComparison.OrdinalIgnoreCase))
                AddBodyAnchorMesh(anchor, record.Renderer, record, record.BellyMask, record.LastNewV, frame);
        FinalizeBodyAnchorContext(anchor);
        return anchor;
    }

    private static void ApplyVisibility(Human human, long stateKey, CharaState state, float rate)
    {
        if (!_records.TryGetValue(stateKey, out var records)) return;
        BeginRebuild(state, "visibility");
        state.VisibilityDirty = false;
        // Body registrations are actor-local even though the lookup scratch set
        // is shared. Another actor may have used it since this actor's last shape.
        BodyMeshSelection.Register(human);
        var visibleBody = ActiveBodyRecords(records);
        bool bodyChanged = state.VisibleBodyRecords == null || !state.VisibleBodyRecords.SetEquals(visibleBody);
        if (bodyChanged)
        {
            // Shared protection reads native skin weights, and both protection
            // and seam shading depend on which body pieces meet. Rebuild these
            // outputs from OrigVerts, never from the previously welded output.
            foreach (var record in visibleBody)
            {
                record.Virtual?.Restore();
                record.Virtual = null;
                record.VirtualAlpha = null;
            }
            ApplySharedBodyProtection(records, state.Frame, rate);
            ApplySkinShading(records, state.Frame);
            state.SurfaceRevision++;
            state.RebuiltBodySurface = true;
            state.VisibleBodyRecords = visibleBody;
            state.BodyAnchor = BuildBodyAnchor(records, state.Frame);
        }
        state.BodyAnchor ??= BuildBodyAnchor(records, state.Frame);
        ApplyClothSMRs(human, state.CharaId, records, state, rate, state.BodyAnchor);
        // Keep the VirtualRig identity and the independent collision cache. Only
        // missing/changed per-mesh bindings are attached at native completion.
        state.VirtualPending = true;
    }
}
