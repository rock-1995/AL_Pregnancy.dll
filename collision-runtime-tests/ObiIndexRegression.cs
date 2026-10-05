using Obi;
using UnityEngine;

namespace ALPregnancy;
internal static partial class BellyVertexMorph
{
    static void RunObiIndexRegression()
    {
        static (ObiColliderWorld World, ObiMeshShapeTracker[] Trackers) Scene()
        {
            var world = new ObiColliderWorld(); ObiColliderWorld.instance = world;
            var trackers = new[] { "belly", "lower-belly", "thigh-left", "thigh-right", "chest" }
                .Select((name, i) => world.Register(new MeshCollider { sharedMesh = new Mesh {
                    name = name, vertices = new[] { new Vector3(i, 1, 2) } } })).ToArray();
            world.UpdateWorld();
            return (world, trackers);
        }
        static Mesh MeshOf(ObiMeshShapeTracker t) => ((MeshCollider)t.collider).sharedMesh;
        DirtyCollisionMeshes.Clear();
        var old = Scene();
        // Reproduce 0.2.22: deletion/recreation occurs INSIDE the tracker iteration.
        // Only the changed belly readers run; static thigh readers keep previous indices.
        foreach (var t in old.Trackers.Take(2)) { t.UpdateMeshData(); t.UpdateIfNeeded(); }
        Check("Old per-tracker refresh reproduces wrong belly mesh index", !old.Trackers[0].Correct);
        Check("Old per-tracker refresh also misbinds unchanged thigh mesh", !old.Trackers[2].Correct);

        // Use the production prefix against a globally compacting native-table model.
        var fixedScene = Scene();
        MarkCollisionDirty(MeshOf(fixedScene.Trackers[0])); MarkCollisionDirty(MeshOf(fixedScene.Trackers[1]));
        fixedScene.World.Dirty = false;
        BeforeCollisionWorldUpdate(fixedScene.World);
        Check("All mesh readers are scheduled after the batch, including static thighs",
            fixedScene.World.Dirty && fixedScene.World.colliderHandles.All(h => h.owner.NeedsUpdate));
        fixedScene.World.UpdateWorld();
        Check("Batch refresh preserves every collider-to-mesh identity", fixedScene.Trackers.All(t => t.Correct));
        Check("Only dirty geometry is invalidated", fixedScene.Trackers.Take(2).All(t => t.Invalidations == 1) && fixedScene.Trackers.Skip(2).All(t => t.Invalidations == 0));
        Check("Successful batch drains matching dirty entries", DirtyCollisionMeshes.Count == 0);

        int rounds = 0; var random = new Random(23);
        for (int i = 0; i < 80; i++)
        {
            // Vary native collider order, changed region, number of characters/meshes,
            // static-reader partition and pose data. No deletion may occur mid-read.
            var order = fixedScene.World.colliderHandles.OrderBy(_ => random.Next()).ToArray();
            fixedScene.World.colliderHandles.Clear(); fixedScene.World.colliderHandles.AddRange(order);
            foreach (var t in fixedScene.Trackers.Take(i % 4 + 1))
            {
                var m = MeshOf(t); m.vertices = new[] { new Vector3(i, t.GetInstanceID(), 3) };
                MarkCollisionDirty(m);
            }
            UpdateObi();
            if (!fixedScene.Trackers.All(t => t.Correct)) throw new Exception("Index mismatch at round " + i);
            foreach (var t in fixedScene.Trackers) { t.Read(); if (!t.Cached.SequenceEqual(MeshOf(t).vertices)) throw new Exception("Stale geometry"); }
            rounds++;
        }
        Check("80 reordered multi-mesh updates keep geometry and indices consistent", rounds == 80);

        var shared = Scene();
        var secondReader = shared.World.Register((MeshCollider)shared.Trackers[0].collider);
        MarkCollisionDirty(MeshOf(shared.Trackers[0])); UpdateObi();
        Check("Shared mesh is invalidated once and both readers get the rebuilt handle",
            shared.Trackers[0].Invalidations + secondReader.Invalidations == 1 && secondReader.Correct && shared.Trackers.All(t => t.Correct));

        var cold = Scene(); var coldMesh = new Mesh { name = "new belly", vertices = new[] { new Vector3(9, 8, 7) } };
        var coldReader = cold.World.Register(new MeshCollider { sharedMesh = coldMesh }, false);
        MarkCollisionDirty(coldMesh); UpdateObi();
        Check("Uninitialized tracker uses native first creation without destroying a null handle", coldReader.Correct && coldReader.Invalidations == 0 && !DirtyCollisionMeshes.ContainsKey(coldMesh.GetInstanceID()));

        var partial = Scene(); partial.Trackers[1].Fail = true;
        MarkCollisionDirty(MeshOf(partial.Trackers[0])); MarkCollisionDirty(MeshOf(partial.Trackers[1]));
        UpdateObi();
        Check("Partial failure still repairs indices already shifted by the first deletion", partial.Trackers.All(t => t.Correct));
        Check("Failed mesh remains pending while successful mesh does not repeat", DirtyCollisionMeshes.Count == 1 && partial.Trackers[0].Invalidations == 1);
        partial.Trackers[1].Fail = false; UpdateObi();
        Check("Next world boundary completes the pending refresh", DirtyCollisionMeshes.Count == 0 && partial.Trackers.All(t => t.Correct));

        var pending = new Mesh { vertices = new[] { new Vector3(3, 4, 5) } };
        MarkCollisionDirty(pending); UpdateObi();
        Check("Mesh with no registered reader remains pending for later activation", DirtyCollisionMeshes.ContainsKey(pending.GetInstanceID()));
        var later = partial.World.Register(new MeshCollider { sharedMesh = pending }); UpdateObi();
        Check("Later activation consumes pending geometry and keeps other meshes valid", later.Correct && DirtyCollisionMeshes.Count == 0 && partial.Trackers.All(t => t.Correct));
        var invalidations = partial.Trackers.Sum(t => t.Invalidations); UpdateObi();
        Check("Clean world causes no extra mesh rebuilding", partial.Trackers.Sum(t => t.Invalidations) == invalidations);
        ObiColliderWorld.instance = new();
    }
}
