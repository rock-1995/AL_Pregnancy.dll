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
    private static readonly HashSet<IntPtr> Reloading = new();
    private static readonly Dictionary<IntPtr, string> Rejections = new();
    private static bool _installed;
    private static IntPtr _nativeBoundary;
    private static readonly List<MeshLease.Observation> BoundaryMeshes = new();
    internal static IReadOnlyList<MeshLease.Observation> CurrentMeshes(Human human) =>
        TryBeginApply(human) ? BoundaryMeshes : null;
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
            _installed = true;
            PregnancyPlugin.Logger.LogInfo($"[Morph readiness] {hooks} character entry hooks; apply at native Human.LateUpdate completion, no time/frame settling or roster barrier.");
        }
        catch (Exception ex)
        {
            _installed = false;
            PregnancyPlugin.Logger.LogError("[Morph readiness] Hooks unavailable; deformation disabled: " + ex);
        }
    }

    private static void BeforeHumanLoad(Human __instance, System.Reflection.MethodBase __originalMethod)
    {
        try
        {
            if (__instance == null) return;
            // These synchronous operations replace meshes. Return native meshes
            // before the game rebuilds its own caches. The completed native update
            // reapplies in the same render cycle, without a visible waiting phase.
            if (__originalMethod.Name is "Load" or "Reload" or "ReloadCoordinate" or "ReloadMannequin")
                InvalidateHuman(__instance, __originalMethod.Name);
            else Reloading.Add(__instance.Pointer);
        }
        catch (Exception ex) { PregnancyPlugin.Logger.LogWarning("[Morph readiness] Reload preparation: " + ex.Message); }
    }

    internal static void InvalidateHuman(Human human, string reason)
    {
        if (human == null) return;
        bool known = Characters.Remove(human.Pointer);
        bool first = Reloading.Add(human.Pointer);
        Status = reason;
        if (!known && !first) return;
        PregnancyRuntime.ReleaseHuman(human);
        PregnancyController.SuspendHuman(human);
    }

    // Called only by our postfix after the real native update has returned.
    // One actor is processed synchronously before the game's caller moves on;
    // another actor being incomplete never prevents this one from completing.
    internal static bool EnterNativeBoundary(Human human)
    {
        if (!_installed || _nativeBoundary != IntPtr.Zero || human == null || human.Disposed ||
            human.IsReloading || human.GameObject == null || !human.GameObject.activeInHierarchy) return false;
        if (!Probe(human, out ulong signature, out var reason))
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
        Reloading.Remove(human.Pointer);
        _nativeBoundary = human.Pointer;
        Status = "Native character update complete";
        return true;
    }
    internal static void ExitNativeBoundary() { _nativeBoundary = IntPtr.Zero; BoundaryMeshes.Clear(); }
    internal static bool TryBeginApply(Human human) => human != null && _nativeBoundary == human.Pointer &&
        !human.Disposed && !human.IsReloading && Characters.ContainsKey(human.Pointer);
    internal static bool CanAnimate(Human human) => TryBeginApply(human);
    internal static void Forget(Human human)
    {
        if (human == null) return;
        Characters.Remove(human.Pointer);
        Reloading.Remove(human.Pointer);
        Rejections.Remove(human.Pointer);
        if (_nativeBoundary == human.Pointer) ExitNativeBoundary();
    }
    private static void Mix(ref ulong hash, long value) => hash = unchecked((hash ^ (ulong)value) * 1099511628211UL);

    private static bool Probe(Human human, out ulong signature, out string reason)
    {
        signature = 14695981039346656037UL;
        BoundaryMeshes.Clear();
        reason = "Body/skin/bones not ready";
        if (human.Disposed || human.IsReloading || human.GameObject == null || human.Body == null) return false;
        Mix(ref signature, human.GameObject.GetInstanceID());
        var renderers = human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool torso = false;
        int populated = 0;
        var bodyIds = new HashSet<int>();
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
            Mix(ref signature, rendererId);
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
            Mix(ref signature, source.GetInstanceID());
            Mix(ref signature, source.vertexCount);
            Mix(ref signature, source.subMeshCount);
            Mix(ref signature, source.blendShapeCount);
            bool body = TorsoSelectionPolicy.IsBodyPiece(bodyIds.Contains(renderer.GetInstanceID()), renderer.name, source.name);
            if (!body || !source.isReadable) continue;
            Transform[] bones = renderer.bones;
            var binds = source.bindposes;
            if (bones == null || binds == null || binds.Length == 0 || bones.Length < binds.Length)
            { reason = $"Body bones/bindposes incomplete: {renderer.name}"; return false; }
            for (int j = 0; j < binds.Length; j++)
            {
                if (bones[j] == null)
                { reason = $"Body bone {j} missing: {renderer.name}"; return false; }
                Mix(ref signature, bones[j].GetInstanceID());
            }
            int waist = BodyMeshSelection.FindBone(bones, BodyMeshSelection.PelvisNames);
            int spine = BodyMeshSelection.FindBone(bones, BodyMeshSelection.SpineNames);
            if (waist >= 0 && spine >= 0 && waist < binds.Length && spine < binds.Length &&
                !(source.name ?? "").Contains("armleg", StringComparison.OrdinalIgnoreCase)) torso = true;
        }
        Mix(ref signature, populated);
        if (populated == 0) { reason = "No populated body/clothing meshes"; return false; }
        if (human.HiPoly && human.Sex == 1 && !torso)
        { reason = "No readable torso with pelvis/spine bindposes"; return false; }
        reason = null;
        return true;
    }
}
