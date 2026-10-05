using Character;
using UnityEngine;
using Obi;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    // Read-only and on demand, using the existing geometry diagnostic button.
    // Never rebuild native collision caches merely to inspect them.
    private static object CollisionSnapshot(Human human)
    {
        try
        {
            var helpers = new List<object>();
            var colliders = new List<object>();
            var roots = new[] { human.GameObject, human.Body?.objHitBody, human.Body?.objHitHead };
            var seenHelpers = new HashSet<int>();
            var seenColliders = new HashSet<int>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var helper in root.GetComponentsInChildren<SkinnedCollisionHelper>(true))
                {
                    if (helper == null || !seenHelpers.Add(helper.GetInstanceID())) continue;
                    var renderer = helper._skinnedMeshRenderer ?? helper.GetComponent<SkinnedMeshRenderer>();
                    var mesh = renderer?.sharedMesh;
                    var collider = helper._meshCollider;
                    CollisionBindings.TryGetValue(helper.GetInstanceID(), out var sync);
                    Transform[] bones = renderer?.bones;
                    helpers.Add(new
                    {
                        name = helper.name, path = PathOf(helper.transform), id = helper.GetInstanceID(),
                        active = helper.gameObject.activeInHierarchy, initialized = helper._isInit,
                        forceUpdate = helper.ForceUpdate, oncePerFrame = helper.UpdateOncePerFrame,
                        renderer = renderer?.name, rendererId = renderer?.GetInstanceID(),
                        sourceMesh = mesh?.name, sourceMeshId = mesh?.GetInstanceID(), owned = MeshLease.Owns(mesh),
                        colliderMeshId = collider?.sharedMesh?.GetInstanceID(), calculatedMeshId = helper._meshCalc?.GetInstanceID(),
                        cachedBoneGroups = helper._nodeWeights?.Length ?? 0,
                        syncMapped = sync?.Mapped ?? 0, syncMoved = sync?.Moved ?? 0, syncMaxWorld = sync?.MaxWorld ?? 0,
                        syncFailed = sync?.Rig.Failed ?? false,
                        calculatedVertices = helper._meshCalc != null && helper._meshCalc.isReadable ? Array.ConvertAll((Vector3[])helper._meshCalc.vertices, Components) : null,
                        obiCachePending = helper._meshCalc != null && DirtyCollisionMeshes.ContainsKey(helper._meshCalc.GetInstanceID()),
                        vertices = mesh != null && mesh.isReadable ? Array.ConvertAll((Vector3[])mesh.vertices, Components) : null,
                        triangles = mesh != null && mesh.isReadable ? (int[])mesh.triangles : null,
                        weights = mesh != null && mesh.isReadable ? Array.ConvertAll((BoneWeight[])mesh.boneWeights,
                            w => new float[] { w.boneIndex0, w.weight0, w.boneIndex1, w.weight1, w.boneIndex2, w.weight2, w.boneIndex3, w.weight3 }) : null,
                        bindposesRowMajor = mesh != null && mesh.isReadable ? Array.ConvertAll((Matrix4x4[])mesh.bindposes,
                            m => MatrixValues(MatrixBridge.ToManaged(m))) : null,
                        boneNames = bones == null ? null : Array.ConvertAll(bones, b => b?.name),
                        boneToWorldRowMajor = bones == null ? null : Array.ConvertAll(bones,
                            b => b == null ? null : MatrixValues(MatrixBridge.ToManaged(b.localToWorldMatrix))),
                        rendererToWorldRowMajor = renderer == null ? null : MatrixValues(MatrixBridge.ToManaged(renderer.transform.localToWorldMatrix))
                    });
                }
                foreach (var obi in root.GetComponentsInChildren<ObiCollider>(true))
                {
                    if (obi == null || !seenColliders.Add(obi.GetInstanceID())) continue;
                    var source = obi.sourceCollider;
                    var meshCollider = source?.TryCast<MeshCollider>();
                    var tracker = obi.Tracker?.TryCast<ObiMeshShapeTracker>();
                    var world = ObiColliderWorld.instance;
                    int shapeIndex = obi.Handle?.index ?? -1;
                    int meshIndex = tracker?.handle?.index ?? -1;
                    int? publishedMeshIndex = world?.colliderShapes != null && shapeIndex >= 0 && shapeIndex < world.colliderShapes.count
                        ? world.colliderShapes[shapeIndex].dataIndex : null;
                    colliders.Add(new
                    {
                        name = obi.name, path = PathOf(obi.transform), id = obi.GetInstanceID(),
                        enabled = obi.enabled, active = obi.gameObject.activeInHierarchy,
                        sourceId = source?.GetInstanceID(), sourceEnabled = source?.enabled,
                        meshId = meshCollider?.sharedMesh?.GetInstanceID(), filter = obi.Filter,
                        thickness = obi.Thickness, material = obi.CollisionMaterial?.name,
                        targetType = obi.Target?.GetIl2CppType()?.FullName,
                        trackerType = obi.Tracker?.GetIl2CppType()?.FullName,
                        shapeIndex, meshIndex, publishedMeshIndex,
                        meshIndexMatches = tracker != null && meshIndex >= 0 && publishedMeshIndex == meshIndex
                    });
                }
            }
            return new { helpers, colliders, mode = "direct low-poly shape; batch cache invalidation before Obi world readers; snapshot read-only", cacheRefreshes = CollisionCacheRefreshes,
                hooksReady = CollisionHooksReady, cacheError = CollisionCacheError,
                syncStates = _state.Values.Where(s => s.HumanPtr == human.Pointer && s.Collision != null).Select(s => new {
                    failed = s.Collision.Failed, writes = s.Collision.Writes, updates = s.Collision.Updates,
                    nativeCalls = s.Collision.NativeCalls, coalescedCalls = s.Collision.CoalescedCalls,
                    lowPolyVertices = s.Collision.Bindings.Sum(b => b.Mapped), highPolyCollisionVertices = 0,
                    meanMs = s.Collision.Updates == 0 ? 0 : s.Collision.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / s.Collision.Updates,
                    maxMs = s.Collision.MaxTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency }).ToArray() };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }
}
