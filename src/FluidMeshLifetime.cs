using FluidFlow;
using UnityEngine;

namespace ALPregnancy;

// FFCanvas caches Surface.Mesh independently of Renderer.sharedMesh. Restore those
// references before destroying a lease's clone, including inactive canvases used by OnEnd.
internal static class FluidMeshLifetime
{
    private sealed class Retired { public Mesh Clone, Original; }
    private static readonly Dictionary<int, Retired> Pending = new();
    private static float _nextCollect;
    private static bool _reportedFailure;

    internal static void Retire(Mesh clone, Mesh original)
    {
        if (clone != null) Pending[clone.GetInstanceID()] = new Retired { Clone = clone, Original = original };
    }
    internal static void Collect(bool force = false)
    {
        if (Pending.Count == 0 || (!force && Time.unscaledTime < _nextCollect)) return;
        _nextCollect = Time.unscaledTime + 1;
        try
        {
            var held = new HashSet<int>();
            int repaired = 0;
            // Resources includes disabled canvases which FindObjectsOfType normally skips.
            foreach (var canvas in Resources.FindObjectsOfTypeAll<FFCanvas>())
            {
                if (canvas == null) continue;
                var surfaces = canvas.Surfaces;
                if (surfaces == null) continue;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    var surface = surfaces[i];
                    var mesh = surface?.Mesh;
                    if (mesh == null || !Pending.TryGetValue(mesh.GetInstanceID(), out var retired)) continue;
                    var original = ResolveOriginal(retired);
                    if (original == null) { held.Add(mesh.GetInstanceID()); continue; }
                    var renderer = surface.Renderer;
                    var uvSet = surface.UVSet;
                    var descriptors = surface.SubmeshDescriptors;
                    surface.Mesh = original;
                    // Surface is a native value type represented by a managed class. The
                    // generated List<T>.set_Item passes its boxed object header as value
                    // data (typeof(T).IsValueType is false), corrupting Renderer and Mesh.
                    // IList accepts a boxed Object; the native List implementation unboxes
                    // it correctly. Do not replace this with surfaces[i] = surface.
                    surfaces.Cast<Il2CppSystem.Collections.IList>()[i] = surface;
                    var written = surfaces[i];
                    if (written == null ||
                        written.Mesh?.Pointer != original.Pointer ||
                        written.Renderer?.Pointer != renderer?.Pointer ||
                        written.UVSet != uvSet ||
                        written.SubmeshDescriptors?.Pointer != descriptors?.Pointer)
                        throw new InvalidOperationException("FluidFlow Surface writeback verification failed; clone retained.");
                    repaired++;
                }
            }
            // Also protect any renderer that acquired the same clone independently.
            foreach (var renderer in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                int id = renderer.sharedMesh.GetInstanceID();
                if (Pending.ContainsKey(id)) held.Add(id);
            }
            foreach (var pair in Pending.ToArray())
            {
                if (held.Contains(pair.Key)) continue;
                if (pair.Value.Clone != null) UnityEngine.Object.Destroy(pair.Value.Clone);
                MeshLease.ForgetOwnership(pair.Key);
                Pending.Remove(pair.Key);
            }
            if (repaired > 0)
                PregnancyPlugin.Logger.LogInfo($"[Mesh lifetime] Restored and verified {repaired} FluidFlow cached mesh reference(s) via native IList before retiring clones.");
            _reportedFailure = false;
        }
        catch (Exception ex)
        {
            // Do not free a mesh when we could not prove the cached references were handed back.
            if (!_reportedFailure) PregnancyPlugin.Logger.LogWarning("[Mesh lifetime] Clone release deferred; will retry: " + ex);
            _reportedFailure = true;
        }
    }
    private static Mesh ResolveOriginal(Retired retired)
    {
        var mesh = retired.Original;
        var seen = new HashSet<int>();
        // Overlapping leases may form clone-of-clone chains. Never hand a surface
        // back to another mesh which this same collection is about to destroy.
        while (mesh != null && Pending.TryGetValue(mesh.GetInstanceID(), out var parent))
        {
            if (!seen.Add(mesh.GetInstanceID())) return null;
            mesh = parent.Original;
        }
        return mesh;
    }
}
