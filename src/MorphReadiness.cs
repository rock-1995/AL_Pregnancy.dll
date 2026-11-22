using Character;
using HarmonyLib;
using UnityEngine;
using GameScene = Manager.Scene;

namespace ALPregnancy;

// Completion comes from the native Human.LateUpdate return, after body shape,
// clothes visibility and shake reset have been committed. No timers/samples.
internal static class MorphReadiness
{
    private static readonly Dictionary<IntPtr, ulong> Characters = new();
    private sealed class MeshSnapshot
    {
        internal IntPtr Body;
        internal IntPtr BodyRoot, BoneRoot, BodyRenderers;
        internal int BodyRendererCount;
        internal int Root;
        internal readonly List<MeshLease.Observation> Meshes = new();
        internal readonly HashSet<int> NonClothingRenderers = new();
    }
    private static readonly Dictionary<IntPtr, MeshSnapshot> Snapshots = new();
    private static readonly HashSet<IntPtr> Reloading = new();
    private static readonly Dictionary<IntPtr, string> Rejections = new();
    private static bool _installed;
    private static IntPtr _nativeBoundary;
    private static bool _boundaryChanged;
    private static IReadOnlyList<MeshLease.Observation> _currentMeshes;
    private static readonly List<MeshLease.Observation> BoundaryMeshes = new();
    private static readonly HashSet<int> BoundaryBodyIds = new();
    private static readonly HashSet<int> BoundaryNonClothingIds = new();
    internal static IReadOnlyList<MeshLease.Observation> CurrentMeshes(Human human) =>
        TryBeginApply(human) ? _currentMeshes : null;
    internal static bool ChangedAtBoundary(Human human) => TryBeginApply(human) && _boundaryChanged;
    // Leasing replaces renderer attachments after Probe. Commit those exact
    // owned meshes so a later native swap back to the same source is observable.
    internal static void RememberLeasedMeshes(Human human)
    {
        if (human == null || !Snapshots.TryGetValue(human.Pointer, out var snapshot)) return;
        for (int i = 0; i < snapshot.Meshes.Count; ++i)
        {
            var old = snapshot.Meshes[i];
            var renderer = old.Renderer;
            var mesh = renderer == null ? null : renderer.sharedMesh;
            snapshot.Meshes[i] = new(renderer, mesh, old.RendererId, mesh == null ? 0 : mesh.GetInstanceID(),
                renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy);
        }
    }
    internal static string Status { get; private set; } = "Waiting for native character update";
    // Used only for housekeeping of retired fluid meshes, never to delay a body.
    internal static bool Ready => _installed && !GameScene.IsNowLoading && !Manager.MapManager.IsLoading;

    internal static void Install(Harmony harmony)
    {
        try
        {
            int hooks = 0;
            foreach (string name in new[] { "Load", "Reload", "ReloadCoordinate", "ReloadHead", "ReloadHair", "ReloadSkin", "ReloadMannequin" })
            {
                var methods = AccessTools.GetDeclaredMethods(typeof(Human)).Where(m => m.Name == name &&
                    !m.GetParameters().Any(p => p.ParameterType.IsByRef)).ToArray();
                if (methods.Length == 0) throw new MissingMethodException(typeof(Human).FullName, name);
                foreach (var method in methods)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(MorphReadiness), nameof(BeforeHumanLoad)));
                    hooks++;
                }
            }
            NativeClothingChanges.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(HumanBody), nameof(HumanBody.Load), new[] { typeof(Transform), typeof(string) }),
                postfix: new HarmonyMethod(typeof(MorphReadiness), nameof(AfterBodyLoad)));
            _installed = true;
            PregnancyPlugin.Logger.LogInfo($"[Morph readiness] {hooks} character entry hooks; apply at native Human.LateUpdate completion, no time/frame settling or roster barrier.");
        }
        catch (Exception ex)
        {
            _installed = false;
            PregnancyPlugin.Logger.LogError("[Morph readiness] Hooks unavailable; deformation disabled: " + ex);
        }
    }

    private static void AfterBodyLoad(HumanBody __instance) => NativeClothingChanges.MarkTopology(__instance?._human);

    private static void BeforeHumanLoad(Human __instance, System.Reflection.MethodBase __originalMethod)
    {
        try
        {
            if (__instance == null) return;
            BodyChangeRuntime.Release(__instance);
            // These synchronous operations replace meshes. Return native meshes
            // before the game rebuilds its own caches. The completed native update
            // reapplies in the same render cycle, without a visible waiting phase.
            if (__originalMethod.Name is "Load" or "Reload" or "ReloadMannequin")
                InvalidateHuman(__instance, __originalMethod.Name);
            else Reloading.Add(__instance.Pointer);
        }
        catch (Exception ex) { PregnancyPlugin.Logger.LogWarning("[Morph readiness] Reload preparation: " + ex.Message); }
    }

    internal static void InvalidateHuman(Human human, string reason)
    {
        if (human == null) return;
        bool known = Characters.Remove(human.Pointer);
        Snapshots.Remove(human.Pointer);
        bool first = Reloading.Add(human.Pointer);
        Status = reason;
        if (!known && !first) return;
        // Native body shape has already committed when Probe invalidates a mesh.
        // Tear down only belly meshes/bindings here: ReleaseHuman would dispose
        // the body-data scope and overwrite that completed pose with card values.
        // Destructive load entry restores body changes in BeforeHumanLoad above.
        PregnancyRuntime.ReleaseMeshes(human);
        PregnancyController.SuspendHuman(human);
    }

    // Called only by our postfix after the real native update has returned.
    // One actor is processed synchronously before the game's caller moves on;
    // another actor being incomplete never prevents this one from completing.
    internal static bool EnterNativeBoundary(Human human)
    {
        if (!_installed || _nativeBoundary != IntPtr.Zero || human == null || human.Disposed ||
            human.IsReloading || human.GameObject == null || !human.GameObject.activeInHierarchy) return false;
        var change = NativeClothingChanges.Consume(human);
        var body = human.Body;
        var bodyRenderers = body?.BodyRenderers;
        bool haveSnapshot = Snapshots.TryGetValue(human.Pointer, out var snapshot) &&
            snapshot.Body == body?.Pointer && snapshot.Root == human.GameObject.GetInstanceID() &&
            snapshot.BodyRoot == (body?._objBody?.Pointer ?? IntPtr.Zero) &&
            snapshot.BoneRoot == (body?._objBone?.Pointer ?? IntPtr.Zero) &&
            snapshot.BodyRenderers == (bodyRenderers?.Pointer ?? IntPtr.Zero) && snapshot.BodyRendererCount == (bodyRenderers?.Length ?? 0) &&
            Characters.ContainsKey(human.Pointer) && !Reloading.Contains(human.Pointer);
        if (snapshot != null && Characters.ContainsKey(human.Pointer))
        {
            change |= CachedRendererChange(snapshot, out bool bodyAttachmentChanged);
            if (bodyAttachmentChanged)
            {
                // Source equivalence is insufficient here: the native renderer
                // has detached our body/overlay clone and its vertex records.
                InvalidateHuman(human, "Native update replaced a body mesh attachment");
                haveSnapshot = false;
            }
        }
        if (haveSnapshot && change == NativeClothingChange.None)
        {
            _currentMeshes = snapshot.Meshes;
            _boundaryChanged = false;
            _nativeBoundary = human.Pointer;
            return true;
        }
        ulong signature;
        string reason;
        bool visibilityOnly = haveSnapshot && (change & NativeClothingChange.Topology) == 0 && RefreshVisibility(snapshot);
        bool ready;
        if (visibilityOnly)
        { signature = Characters[human.Pointer]; reason = null; ready = true; }
        else ready = Probe(human, out signature, out reason);
        if (!ready)
        {
            if (!Rejections.TryGetValue(human.Pointer, out string previousReason) || previousReason != reason)
                PregnancyPlugin.Logger.LogWarning($"[Morph readiness] actor={human.GameObject.GetInstanceID()}: {reason}");
            Rejections[human.Pointer] = reason;
            InvalidateHuman(human, reason);
            BoundaryMeshes.Clear();
            return false;
        }
        if (Rejections.Remove(human.Pointer))
            PregnancyPlugin.Logger.LogInfo($"[Morph readiness] actor={human.GameObject.GetInstanceID()}: body/skin/bones now ready at native completion.");
        if (Characters.TryGetValue(human.Pointer, out ulong previous) && previous != signature)
            InvalidateHuman(human, "Native update committed a changed mesh/skeleton");
        Characters[human.Pointer] = signature;
        var roles = visibilityOnly ? snapshot.NonClothingRenderers : BoundaryNonClothingIds;
        snapshot = new MeshSnapshot { Body = body.Pointer, Root = human.GameObject.GetInstanceID(),
            BodyRoot = body._objBody?.Pointer ?? IntPtr.Zero, BoneRoot = body._objBone?.Pointer ?? IntPtr.Zero,
            BodyRenderers = bodyRenderers?.Pointer ?? IntPtr.Zero, BodyRendererCount = bodyRenderers?.Length ?? 0 };
        snapshot.Meshes.AddRange(BoundaryMeshes);
        snapshot.NonClothingRenderers.UnionWith(roles);
        Snapshots[human.Pointer] = snapshot;
        Reloading.Remove(human.Pointer);
        _currentMeshes = snapshot.Meshes;
        _boundaryChanged = true;
        _nativeBoundary = human.Pointer;
        Status = "Native character update complete";
        return true;
    }
    internal static void ExitNativeBoundary() { _nativeBoundary = IntPtr.Zero; _currentMeshes = null; _boundaryChanged = false; BoundaryMeshes.Clear(); }
    internal static bool TryBeginApply(Human human) => human != null && _nativeBoundary == human.Pointer &&
        !human.Disposed && !human.IsReloading && Characters.ContainsKey(human.Pointer);
    internal static bool CanAnimate(Human human) => TryBeginApply(human);
    internal static void Forget(Human human)
    {
        if (human == null) return;
        Characters.Remove(human.Pointer);
        Snapshots.Remove(human.Pointer);
        NativeClothingChanges.Forget(human);
        Reloading.Remove(human.Pointer);
        Rejections.Remove(human.Pointer);
        if (_nativeBoundary == human.Pointer) ExitNativeBoundary();
    }
    private static void Mix(ref ulong hash, long value) => hash = unchecked((hash ^ (ulong)value) * 1099511628211UL);

    private static NativeClothingChange CachedRendererChange(MeshSnapshot snapshot, out bool bodyAttachmentChanged)
    {
        // Native ChangeClothesTop also schedules a next-frame SetActive(false)
        // callback, and Magica can exchange meshes without a clothing callback.
        // Inspect cached attachment identities and visibility, not mesh arrays,
        // bone arrays or the hierarchy. Continue after a cloth change so a body
        // attachment change in the same native pass cannot be overlooked.
        bodyAttachmentChanged = false;
        var result = NativeClothingChange.None;
        foreach (var item in snapshot.Meshes)
        {
            var mesh = item.Renderer == null ? null : item.Renderer.sharedMesh;
            if (item.Renderer == null || (mesh == null ? 0 : mesh.GetInstanceID()) != item.MeshId)
            {
                result |= NativeClothingChange.Topology;
                bodyAttachmentChanged |= snapshot.NonClothingRenderers.Contains(item.RendererId);
                continue;
            }
            if (item.Active != (item.Renderer.enabled && item.Renderer.gameObject.activeInHierarchy))
                result |= NativeClothingChange.Visibility;
        }
        return result;
    }

    private static bool RefreshVisibility(MeshSnapshot snapshot)
    {
        BoundaryMeshes.Clear();
        foreach (var old in snapshot.Meshes)
        {
            var renderer = old.Renderer;
            if (renderer == null) return false;
            var mesh = renderer.sharedMesh;
            if ((mesh == null ? 0 : mesh.GetInstanceID()) != old.MeshId) return false;
            BoundaryMeshes.Add(new(renderer, mesh, old.RendererId, mesh == null ? 0 : mesh.GetInstanceID(),
                renderer.enabled && renderer.gameObject.activeInHierarchy));
        }
        return true;
    }

    private static bool Probe(Human human, out ulong signature, out string reason)
    {
        signature = 14695981039346656037UL;
        BoundaryMeshes.Clear();
        BoundaryNonClothingIds.Clear();
        reason = "Body/skin/bones not ready";
        if (human.Disposed || human.IsReloading || human.GameObject == null || human.Body == null) return false;
        Mix(ref signature, human.GameObject.GetInstanceID());
        var renderers = human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool torso = false;
        int populated = 0, retainedBodyMeshes = 0;
        BodyMeshSelection.Register(human);
        var bodyIds = BoundaryBodyIds;
        bodyIds.Clear();
        var bodies = human.Body.BodyRenderers;
        if (bodies != null)
            foreach (var entry in bodies)
                if (entry?.Renderer != null) bodyIds.Add(entry.Renderer.GetInstanceID());
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var mesh = renderer.sharedMesh;
            if (!MeshLease.Relevant(renderer, mesh)) continue;
            var source = MeshLease.OriginalForReadiness(mesh);
            bool active = renderer.enabled && renderer.gameObject.activeInHierarchy;
            int rendererId = renderer.GetInstanceID();
            BoundaryMeshes.Add(new(renderer, mesh, rendererId, mesh == null ? 0 : mesh.GetInstanceID(), active));
            if (source == null || source.vertexCount == 0)
            {
                // Optional accessory slots can legitimately have no mesh. Body
                // registrations are required; late optional arrivals invalidate
                // only this actor at the same completed native update.
                if (active && bodyIds.Contains(renderer.GetInstanceID()))
                { reason = $"Registered body mesh missing/empty: {renderer.name}"; return false; }
                continue;
            }
            populated++;
            // Clothing topology has its own incremental lease update. Keep body
            // pieces and overlays in this signature: replacing those still needs
            // a complete calibration/skin/rig rebuild.
            if (BellyVertexMorph.IsClothingRenderer(renderer)) continue;
            BoundaryNonClothingIds.Add(rendererId);
            retainedBodyMeshes++;
            Mix(ref signature, rendererId);
            Mix(ref signature, source.GetInstanceID());
            Mix(ref signature, source.vertexCount);
            Mix(ref signature, source.subMeshCount);
            Mix(ref signature, source.blendShapeCount);
            bool body = TorsoSelectionPolicy.IsBodyPiece(bodyIds.Contains(renderer.GetInstanceID()), renderer.name, source.name);
            if (!body || !source.isReadable) continue;
            Transform[] bones = renderer.bones;
            // Only the count is used here. Do not copy every bind-pose matrix
            // across the native boundary just to inspect the array length.
            int bindCount = source.bindposeCount;
            if (bones == null || bindCount == 0 || bones.Length < bindCount)
            { reason = $"Body bones/bindposes incomplete: {renderer.name}"; return false; }
            for (int j = 0; j < bindCount; j++)
            {
                if (bones[j] == null)
                { reason = $"Body bone {j} missing: {renderer.name}"; return false; }
                Mix(ref signature, bones[j].GetInstanceID());
            }
            int waist = BodyMeshSelection.FindBone(bones, BodyMeshSelection.PelvisNames);
            int spine = BodyMeshSelection.FindBone(bones, BodyMeshSelection.SpineNames);
            if (waist >= 0 && spine >= 0 && waist < bindCount && spine < bindCount &&
                !(source.name ?? "").Contains("armleg", StringComparison.OrdinalIgnoreCase)) torso = true;
        }
        Mix(ref signature, retainedBodyMeshes);
        if (populated == 0) { reason = "No populated body/clothing meshes"; return false; }
        if (human.HiPoly && human.Sex == 1 && !torso)
        { reason = "No readable torso with pelvis/spine bindposes"; return false; }
        reason = null;
        return true;
    }
}
