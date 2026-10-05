using Character;
using HarmonyLib;
using UnityEngine;
using Obi;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using WeightList = SkinnedCollisionHelper.WeightList;
using VertexWeight = SkinnedCollisionHelper.VertexWeight;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private sealed class CollisionBinding
    {
        internal CollisionRig Rig;
        internal int HelperId, CalculatedId;
        internal SkinnedCollisionHelper Helper;
        internal SkinnedMeshRenderer Renderer;
        internal Mesh SourceMesh, Calculated;
        internal MeshCollider Collider;
        internal Il2CppReferenceArray<WeightList> OriginalCache, Cache;
        internal Il2CppSystem.Collections.Generic.List<VertexWeight> DynamicWeights;
        internal Transform Carrier;
        internal VirtualBinding Skin;
        internal NMatrix ToReference;
        internal CollisionDeformation Shape;
        internal NVector[] LastWritten;
        internal int Mapped, Moved;
        internal float MaxWorld;
        internal bool Written, Dispatch, Submitted;
        internal bool NativeOutput => Helper != null && Helper._isInit && Helper._meshCalc == Calculated &&
            Collider != null && Collider.sharedMesh == Calculated && Helper._newVert?.Length == Shape.Original.Length;
        internal bool NativeIdentity => NativeOutput && Renderer != null && Renderer.sharedMesh == SourceMesh;
        internal bool Valid => NativeIdentity && Helper._nodeWeights?.Pointer == Cache?.Pointer;
    }
    private sealed class CollisionRig
    {
        internal CharaState State;
        internal VirtualRig ExpectedVirtual;
        internal CollisionBinding[] Bindings = Array.Empty<CollisionBinding>();
        internal int Revision;
        internal bool Ready, Failed;
        internal long Writes, Updates, Ticks, MaxTicks, NativeCalls, CoalescedCalls;
    }
    private static readonly Dictionary<int, CollisionBinding> CollisionBindings = new();
    private static readonly Dictionary<int, Mesh> DirtyCollisionMeshes = new();
    private static readonly Dictionary<int, int> NativeCollisionMeshes = new();
    private static int CollisionRevision;
    private static long CollisionCacheRefreshes;
    private static bool CollisionHooksReady, CollisionCacheError;

    internal static void InstallCollisionSync(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SkinnedCollisionHelper), nameof(SkinnedCollisionHelper.UpdateCollisionMesh)),
            prefix: new HarmonyMethod(typeof(BellyVertexMorph), nameof(BeforeNativeCollision)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(BellyVertexMorph), nameof(AfterNativeCollision)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(SkinnedCollisionHelper), nameof(SkinnedCollisionHelper.Init)),
            postfix: new HarmonyMethod(typeof(BellyVertexMorph), nameof(CollisionTopologyChanged)));
        harmony.Patch(AccessTools.Method(typeof(SkinnedCollisionHelper), nameof(SkinnedCollisionHelper.Release)),
            prefix: new HarmonyMethod(typeof(BellyVertexMorph), nameof(CollisionHelperReleasing)));
        // DestroyTriangleMesh compacts the GLOBAL mesh table. Finish all invalidations
        // before any collider publishes its dataIndex in this world update.
        harmony.Patch(AccessTools.Method(typeof(ObiColliderWorld), nameof(ObiColliderWorld.UpdateWorld)),
            prefix: new HarmonyMethod(typeof(BellyVertexMorph), nameof(BeforeCollisionWorldUpdate)) { priority = Priority.First });
        CollisionHooksReady = true;
    }
    private static void CollisionTopologyChanged(SkinnedCollisionHelper __instance)
    {
        if (__instance == null || !__instance._isInit || __instance._meshCalc == null) return;
        int id = __instance.GetInstanceID(), mesh = __instance._meshCalc.GetInstanceID();
        if (NativeCollisionMeshes.TryGetValue(id, out int previous) && previous == mesh) return;
        NativeCollisionMeshes[id] = mesh; CollisionRevision++;
    }
    private static void CollisionHelperReleasing(SkinnedCollisionHelper __instance)
    {
        CollisionRevision++;
        if (__instance == null) return;
        int id = __instance.GetInstanceID();
        if (CollisionBindings.Remove(id, out var b)) RestoreCollisionCache(b);
        NativeCollisionMeshes.Remove(id);
        if (__instance._meshCalc != null) DirtyCollisionMeshes.Remove(__instance._meshCalc.GetInstanceID());
    }
    private static void BeforeCollisionWorldUpdate(ObiColliderWorld __instance)
    {
        if (DirtyCollisionMeshes.Count == 0 || __instance == null) return;
        var readers = new List<(ObiColliderHandle Handle, ObiMeshShapeTracker Tracker)>();
        bool refreshReaders = false;
        try
        {
            var handles = __instance.colliderHandles;
            if (handles == null) return;
            // MarkColliderAsNeedingUpdate reorders colliderHandles. Snapshot first.
            for (int i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                var tracker = handle?.owner?.Tracker?.TryCast<ObiMeshShapeTracker>();
                if (tracker != null) readers.Add((handle, tracker));
            }
            foreach (var reader in readers)
            {
                var mesh = reader.Tracker.collider?.TryCast<MeshCollider>()?.sharedMesh;
                if (mesh == null || !DirtyCollisionMeshes.TryGetValue(mesh.GetInstanceID(), out var dirty) || dirty != mesh) continue;
                // One invalidation per mesh, including when several colliders share it.
                // A null handle needs only its first native creation, using current vertices.
                refreshReaders = true;
                if (reader.Tracker.handle != null) reader.Tracker.UpdateMeshData();
                DirtyCollisionMeshes.Remove(mesh.GetInstanceID()); CollisionCacheRefreshes++;
            }
            CollisionCacheError = false;
        }
        catch (Exception e)
        {
            if (!CollisionCacheError) Log.LogError("[Body collision] Obi cache refresh failed: " + e);
            CollisionCacheError = true;
        }
        finally
        {
            if (refreshReaders)
            {
                // Even UNCHANGED/static mesh colliders can have shifted indices. Force
                // them through the upcoming native pass, including after a partial failure.
                foreach (var reader in readers) __instance.MarkColliderAsNeedingUpdate(reader.Handle);
                __instance.SetDirty();
            }
        }
    }
    private static void MarkCollisionDirty(Mesh mesh)
    { if (mesh != null) DirtyCollisionMeshes[mesh.GetInstanceID()] = mesh; }

    private static void ReleaseCollision(CharaState state)
    {
        var rig = state.Collision; state.Collision = null;
        if (rig != null) RestoreCollisionRig(rig);
    }
    private static bool RestoreCollisionCache(CollisionBinding b)
    {
        // Never put an old cache over a cache replaced by native Init/another owner.
        if (b.Helper == null || b.Helper._nodeWeights?.Pointer != b.Cache?.Pointer) return false;
        b.Helper._nodeWeights = b.OriginalCache;
        return true;
    }
    private static void RestoreCollisionRig(CollisionRig rig)
    {
        rig.Ready = false;
        foreach (var b in rig.Bindings)
        {
            if (CollisionBindings.TryGetValue(b.HelperId, out var current) && ReferenceEquals(current, b)) CollisionBindings.Remove(b.HelperId);
            try
            {
                bool restored = RestoreCollisionCache(b);
                if (restored && b.Written && b.NativeOutput)
                { b.Helper.UpdateCollisionMesh(); MarkCollisionDirty(b.Calculated); }
            }
            catch (Exception e) { Log.LogWarning("[Body collision] Native restore: " + e.Message); }
            if (b.Helper == null) NativeCollisionMeshes.Remove(b.HelperId);
            if (b.Calculated == null || b.Helper == null || b.Helper._meshCalc != b.Calculated) DirtyCollisionMeshes.Remove(b.CalculatedId);
        }
    }

    private static NMatrix CollisionNativeSkin(VirtualBinding binding, int i)
    {
        var w = binding.Weights[i]; var native = new NMatrix();
        if (w.weight0 > 0) native += NativeMatrix(binding, w.boneIndex0) * w.weight0;
        if (w.weight1 > 0) native += NativeMatrix(binding, w.boneIndex1) * w.weight1;
        if (w.weight2 > 0) native += NativeMatrix(binding, w.boneIndex2) * w.weight2;
        if (w.weight3 > 0) native += NativeMatrix(binding, w.boneIndex3) * w.weight3;
        return native;
    }

    private static void BuildCollisionCache(CollisionBinding b, bool dynamic)
    {
        var shape = b.Shape; int n = shape.Original.Length;
        var affected = new bool[n]; foreach (int i in shape.Indices) affected[i] = true;
        var covered = new float[n]; var groups = new List<WeightList>();
        for (int g = 0; g < b.OriginalCache.Length; g++)
        {
            var old = b.OriginalCache[g];
            if (old == null || old.Transform == null || old.Weights == null) throw new InvalidOperationException("Incomplete native collision cache.");
            var entries = new Il2CppSystem.Collections.Generic.List<VertexWeight>();
            int bone = Array.FindIndex(b.Skin.Bones, t => t == old.Transform);
            for (int j = 0; j < old.Weights.Count; j++)
            {
                var item = old.Weights[j]; int i = item.Index;
                if ((uint)i >= n || !float.IsFinite(item.Weight) || item.Weight < 0) throw new InvalidOperationException("Invalid native collision weight.");
                covered[i] += item.Weight;
                if (affected[i])
                {
                    if (dynamic) continue;
                    if (bone < 0) throw new InvalidOperationException("Collision cache bone missing from skin.");
                    // Static-only deformation is entirely baked into native input once.
                    item.LocalPosition = ToUnity(NVector.Transform(shape.Static[i], MatrixBridge.ToManaged(b.Skin.Binds[bone])));
                }
                entries.Add(item);
            }
            // Keep every unmodified entry/normal/weight exactly as the game made it.
            groups.Add(new WeightList { Transform = old.Transform, Weights = entries });
        }
        foreach (int i in shape.Indices)
            if (MathF.Abs(covered[i] - 1) > 1e-3f) throw new InvalidOperationException("Collision skin weights are not normalized.");
        if (dynamic)
        {
            b.Carrier = b.Rig.ExpectedVirtual.Pelvis;
            b.DynamicWeights = new Il2CppSystem.Collections.Generic.List<VertexWeight>();
            foreach (int i in shape.Indices)
                b.DynamicWeights.Add(new VertexWeight { Index = i, Weight = 1 });
            groups.Add(new WeightList { Transform = b.Carrier, Weights = b.DynamicWeights });
        }
        b.Cache = new Il2CppReferenceArray<WeightList>(groups.ToArray());
    }

    private static void PrepareCollision(Human human, CharaState state, List<MeshRecord> records)
    {
        ReleaseCollision(state);
        if (!CollisionHooksReady || state.Profile == null || state.LastAppliedRate <= 0) return;
        var rig = new CollisionRig { State = state, ExpectedVirtual = state.Virtual, Revision = CollisionRevision };
        state.Collision = rig;
        var reference = FindRecord(records, state.SMR.sharedMesh);
        var refBones = reference?.Virtual?.Bones ?? (Transform[])state.SMR.bones;
        var refBinds = reference?.Virtual?.Binds ?? (Matrix4x4[])state.SMR.sharedMesh.bindposes;
        var refNames = refBones.Select(b => b?.name ?? "").ToArray(); var refPoses = refBinds.Select(MatrixBridge.ToManaged).ToArray();
        var seen = new HashSet<int>(); var bindings = new List<CollisionBinding>();
        bool dynamic = state.Virtual != null && !state.Virtual.Failed;
        foreach (var root in new[] { human.GameObject, human.Body?.objHitBody })
        {
            if (root == null) continue;
            foreach (var helper in root.GetComponentsInChildren<SkinnedCollisionHelper>(true))
            {
                if (helper == null || !seen.Add(helper.GetInstanceID()) || !helper._isInit || helper._meshCalc == null || helper._meshCollider == null || helper._nodeWeights == null) continue;
                var renderer = helper._skinnedMeshRenderer; var mesh = renderer?.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Transform[] bones = renderer.bones; Matrix4x4[] binds = mesh.bindposes; BoneWeight[] weights = mesh.boneWeights;
                var names = bones.Select(b => b?.name ?? "").ToArray(); var poses = binds.Select(MatrixBridge.ToManaged).ToArray();
                if (!RestSpaceMapping.TryCreate(names, poses, refNames, refPoses, out var map, out _, out _, out _) &&
                    !RestSpaceMapping.TryCreateAliased(names, poses, refNames, refPoses, out map, out _, out _, out _)) continue;
                var vertices = (Vector3[])mesh.vertices; var original = ToManagedVectors(vertices);
                if (helper._newVert?.Length != original.Length || weights.Length != original.Length || bones.Length != binds.Length) continue;
                var belly = new HashSet<int>(); var legs = new HashSet<int>();
                for (int i = 0; i < names.Length; i++)
                {
                    var name = names[i];
                    if (name.Contains("waist") || name.Contains("spine") || name.Contains("hip") || name.Contains("belly")) belly.Add(i);
                    if (IsLegBoneName(name)) legs.Add(i);
                }
                var welds = ComputeNormalWeldGroup(vertices);
                var excluded = BreastExclusion.Build(ComputeBreastWeights(mesh, original.Length, renderer), original.Length, welds, BellyDeformSettings.Vtx.BreastExclusionEnabled);
                var fold = dynamic && state.Virtual.Fold?.Failed == false ? state.Virtual.Fold : null;
                var shape = CollisionDeformation.Build(original, map, FrameMatrix(state.Frame), state.Profile, state.LastAppliedRate, BellyDeformSettings.Vtx,
                    ComputeBellyInfluence(mesh, original.Length, belly, legs), excluded, dynamic,
                    fold == null ? null : fold.Field.Contains);
                if (shape.Indices.Length == 0) continue;
                var skin = new VirtualBinding { Bones = bones, Binds = binds, Weights = weights,
                    NativeMatrices = new NMatrix[bones.Length], NativeTicks = new int[bones.Length] };
                var b = new CollisionBinding { Rig = rig, Helper = helper, HelperId = helper.GetInstanceID(), CalculatedId = helper._meshCalc.GetInstanceID(),
                    Renderer = renderer, SourceMesh = mesh, Calculated = helper._meshCalc, Collider = helper._meshCollider,
                    OriginalCache = helper._nodeWeights, Skin = skin, ToReference = map, Shape = shape, Mapped = shape.Indices.Length, LastWritten = new NVector[original.Length] };
                BuildCollisionCache(b, dynamic);
                bindings.Add(b);
            }
        }
        // Register every restore path before installing any cache. Dynamic
        // carrier positions are fully validated before the first native update.
        rig.Bindings = bindings.ToArray();
        foreach (var b in rig.Bindings) UpdateCollisionInputs(b);
        foreach (var b in rig.Bindings) { CollisionBindings[b.HelperId] = b; b.Helper._nodeWeights = b.Cache; }
        rig.Ready = true;
        Log.LogInfo($"[Body collision] direct low-poly deformation: {bindings.Sum(b => b.Mapped)} affected vertices on {bindings.Count} helpers; no high-poly displacement sampling; one native commit at completed Human update.");
    }

    private static void UpdateCollisionInputs(CollisionBinding b)
    {
        if (b.DynamicWeights == null) return;
        var state = b.Rig.State; var axis = state.Virtual; var shape = b.Shape;
        if (axis == null || axis.Failed || b.Carrier == null) throw new InvalidOperationException("Collision virtual carrier unavailable.");
        if (!NMatrix.Invert(MatrixBridge.ToManaged(b.Carrier.localToWorldMatrix), out var inverseCarrier))
            throw new InvalidOperationException("Singular collision carrier.");
        var virtualMesh = b.ToReference * axis.Last.Transform;
        var fold = axis.Fold; bool support = fold != null && !fold.Failed;
        NMatrix frameWorld = NMatrix.Identity, inverseFrame = NMatrix.Identity;
        if (support)
        {
            frameWorld = fold.Frame * axis.PelvisBind * MatrixBridge.ToManaged(axis.Pelvis.localToWorldMatrix);
            if (!NMatrix.Invert(frameWorld, out inverseFrame)) throw new InvalidOperationException("Singular collision support frame.");
        }
        b.Skin.Tick++; b.Moved = 0; b.MaxWorld = 0;
        Array.Clear(shape.Support, 0, shape.Support.Length);
        foreach (int i in shape.Indices)
        {
            var native = CollisionNativeSkin(b.Skin, i);
            var skin = NMatrix.Lerp(native, virtualMesh, shape.Blend[i]);
            var world = NVector.Transform(shape.Static[i], skin);
            if (!float.IsFinite(world.LengthSquared())) throw new InvalidOperationException("Nonfinite low-poly pose.");
            shape.World[i] = world;
            if (support && shape.Candidates[i])
                shape.Support[i] = fold.Field.Correction(shape.Material[i], NVector.Transform(world, inverseFrame) / state.Profile.Span, false);
        }
        foreach (int i in shape.Indices)
        {
            var final = shape.World[i] + NVector.TransformNormal(shape.Support[i] * state.Profile.Span, frameWorld);
            var local = NVector.Transform(final, inverseCarrier);
            if (!float.IsFinite(local.LengthSquared())) throw new InvalidOperationException("Invalid collision carrier position.");
            shape.CarrierLocal[i] = local;
            float moved = NVector.Distance(final, NVector.Transform(shape.Original[i], CollisionNativeSkin(b.Skin, i)));
            if (moved > 1e-7f) { b.Moved++; b.MaxWorld = MathF.Max(b.MaxWorld, moved); }
        }
        // All requested positions passed validation; never leave half a pose in
        // the native cache if a support calculation fails.
        for (int j = 0; j < shape.Indices.Length; j++)
        {
            var item = b.DynamicWeights[j]; item.LocalPosition = ToUnity(shape.CarrierLocal[shape.Indices[j]]);
            b.DynamicWeights[j] = item;
        }
    }

    internal static void UpdateBodyCollision(Human human)
    {
        if (human == null) return;
        foreach (var pair in _state)
        {
            var state = pair.Value;
            if (state.HumanPtr != human.Pointer || !_records.TryGetValue(pair.Key, out var records)) continue;
            var rig = state.Collision;
            if (rig?.Failed == true || state.LastAppliedRate <= 0 || !float.IsFinite(state.LastAppliedRate)) continue;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (rig == null || rig.Revision != CollisionRevision || rig.Bindings.Any(b => !b.Valid) ||
                    rig.ExpectedVirtual != state.Virtual || (state.Virtual?.Failed == true && rig.Bindings.Any(b => b.DynamicWeights != null)))
                { PrepareCollision(human, state, records); rig = state.Collision; }
                if (rig == null || !rig.Ready) continue;
                foreach (var b in rig.Bindings)
                {
                    if (!b.Valid || !b.Helper.enabled || !b.Helper.gameObject.activeInHierarchy || !b.Collider.enabled) continue;
                    b.Dispatch = true;
                    try { b.Helper.UpdateCollisionMesh(); }
                    finally { b.Dispatch = false; }
                }
            }
            catch (Exception e)
            {
                rig ??= state.Collision ?? new CollisionRig();
                ReleaseCollision(state); rig.Failed = true; state.Collision = rig;
                Log.LogError("[Body collision] Sync disabled and native cache restored; visual shape retained: " + e);
            }
            finally
            {
                if (rig != null) { long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started; rig.Ticks += ticks; rig.Updates++; rig.MaxTicks = Math.Max(rig.MaxTicks, ticks); }
            }
        }
    }

    private static bool BeforeNativeCollision(SkinnedCollisionHelper __instance)
    {
        if (__instance == null || !CollisionBindings.TryGetValue(__instance.GetInstanceID(), out var b)) return true;
        b.Submitted = false;
        if (!b.Valid || !b.Rig.Ready)
        { RestoreCollisionRig(b.Rig); return true; }
        // Own just this bound helper's scheduling: every completed Human update
        // dispatches once with the final visual pose. Earlier/later helper native
        // entries are coalesced, with no frame counter, timer or reduced cadence.
        if (!b.Dispatch) { b.Rig.CoalescedCalls++; return false; }
        try { UpdateCollisionInputs(b); b.Submitted = true; b.Rig.NativeCalls++; return true; }
        catch (Exception e)
        {
            b.Rig.Failed = true; RestoreCollisionRig(b.Rig);
            Log.LogError("[Body collision] Low-poly input failed; native cache restored: " + e);
            // Restoration already recomputed the native pose when previously written.
            return !b.Written;
        }
    }
    private static void AfterNativeCollision(SkinnedCollisionHelper __instance)
    {
        if (__instance == null || !CollisionBindings.TryGetValue(__instance.GetInstanceID(), out var b) || !b.Submitted || !b.Valid) return;
        b.Submitted = false;
        try
        {
            bool changed = !b.Written; var vertices = __instance._newVert;
            for (int i = 0; i < b.LastWritten.Length; i++)
            {
                var value = vertices[i]; var point = new NVector(value.x, value.y, value.z);
                if (!float.IsFinite(point.LengthSquared())) throw new InvalidOperationException("Invalid native collision output.");
                changed |= NVector.DistanceSquared(point, b.LastWritten[i]) > 1e-14f; b.LastWritten[i] = point;
            }
            b.Written = true; b.Rig.Writes++;
            // Native code already wrote/cooked the FINAL shape. No vertices,
            // bounds or sharedMesh resubmission here. Only notify the Obi reader.
            if (changed) MarkCollisionDirty(b.Calculated);
        }
        catch (Exception e)
        {
            b.Written = true; b.Rig.Failed = true; RestoreCollisionRig(b.Rig);
            Log.LogError("[Body collision] Native result failed validation: " + e);
        }
    }

    internal static void FinishCollisionSync()
    {
        foreach (var state in _state.Values) ReleaseCollision(state);
        // Use the existing world only: unloading must not create a new Obi world.
        BeforeCollisionWorldUpdate(ObiColliderWorld.instance);
        CollisionBindings.Clear(); DirtyCollisionMeshes.Clear(); NativeCollisionMeshes.Clear(); CollisionHooksReady = false;
    }
}

