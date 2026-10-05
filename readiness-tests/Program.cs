using ALPregnancy;
using Character;
using UnityEngine;

int checks = 0;
void Check(string name, bool value) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
Human Character(bool complete = true)
{
    var h = new Human();
    if (complete) Complete(h);
    Human.Registry.Items.Add(h);
    return h;
}
void Complete(Human h)
{
    var renderer = new SkinnedMeshRenderer { name = "o_lower_type04", sharedMesh = new Mesh { name = "o_lower_type04" } };
    h.GameObject.Renderers = new[] { renderer };
    h.Body.BodyRenderers = new[] { new BodyEntry { Renderer = renderer } };
}
bool Begin(Human h) => MorphReadiness.EnterNativeBoundary(h);
void End() => MorphReadiness.ExitNativeBoundary();
void Prefix(Human h, string method) => typeof(MorphReadiness).GetMethod("BeforeHumanLoad", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
    .Invoke(null, new object[] { h, typeof(Human).GetMethod(method, Type.EmptyTypes)! });
MorphReadiness.Install(new HarmonyLib.Harmony());
Check("Character entry hooks install", HarmonyLib.Harmony.Hooks >= 7);
Check("No unsupported by-ref reload ABI hook", !HarmonyLib.Harmony.PatchedByRef);
var a = Character(); var b = Character(false);
Check("An otherwise complete actor cannot apply outside native completion", !MorphReadiness.TryBeginApply(a));
Check("First completed native call admits an actor at time/frame zero", Begin(a) && MorphReadiness.TryBeginApply(a));
Check("Incomplete neighbour does not prevent admission", !MorphReadiness.TryBeginApply(b));
Check("Nested callback cannot change the current native owner", !Begin(b) && MorphReadiness.TryBeginApply(a));
End();
Check("Leaving native completion revokes mutation permission", !MorphReadiness.TryBeginApply(a));
Check("Incomplete character is rejected on its own boundary", !Begin(b));
Complete(b);
Check("Completing meshes needs no additional frame or elapsed time", Begin(b) && MorphReadiness.TryBeginApply(b)); End();
Check("Both complete actors are served at the same unchanged frame/time", Time.frameCount == 0 && Time.unscaledTime == 0 && Begin(a)); End();

var lease = new MeshLease(); var source = a.GameObject.Renderers[0].sharedMesh; lease.Acquire(a);
var ar = a.GameObject.Renderers[0]; var clone = ar.sharedMesh;
Check("Own clone is recognized as the unchanged native source", Begin(a) && ar.sharedMesh == clone); End();
ar.bones = ar.bones.Concat(new[] { new Transform() }).ToArray();
Check("Appended virtual palette bones are excluded from native fingerprint", Begin(a)); End();
int beforeA = PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer);
Manager.Scene.IsNowLoading = Manager.Scene.IsNowLoadingFade = Manager.MapManager.IsLoading = true;
AL.Scene.ActionMapScene.Instance = new(); AL.Scene.ActionMapScene.Initialized = false;
AL.H.HScene.Instance = new() { Loaded = false };
Check("Scene loading does not freeze completed character pose/bounds", Begin(a) && MorphReadiness.CanAnimate(a)); End();
Check("Housekeeping can pause independently of character completion", !MorphReadiness.Ready);
Check("Scene changes retain the exact private body mesh", ar.sharedMesh == clone && PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA);

b.IsReloading = true;
Check("A native reloading actor cannot apply", !Begin(b));
Check("Another actor can finish while its neighbour reloads", Begin(a)); End();
b.IsReloading = false;
Check("Native reload completion admits immediately", Begin(b)); End();
Prefix(a, "ReloadHair");
Check("Cosmetic reload retains the body lease", PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA && ar.sharedMesh == clone);
Check("First post-cosmetic native update resumes immediately", Begin(a)); End();
int beforeB = PregnancyRuntime.HumanReleases.GetValueOrDefault(b.Pointer);
Prefix(b, "ReloadCoordinate"); Prefix(b, "Reload");
Check("Nested destructive reload entry restores the actor once", PregnancyRuntime.HumanReleases.GetValueOrDefault(b.Pointer) == beforeB + 1);
Check("Reload entry never restores an unrelated actor", PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA);
Check("Coordinate rebuild completes in its next real native boundary", Begin(b)); End();

ar.enabled = false;
Check("Native visibility completion keeps lease and readiness", Begin(a));
Check("Visibility change refreshes mapping exactly once", lease.CheckChanges(MorphReadiness.CurrentMeshes(a)).Visibility && !lease.CheckChanges(MorphReadiness.CurrentMeshes(a)).Visibility); End();
ar.enabled = true;
Check("Revealed skin retains its private mesh", Begin(a) && ar.sharedMesh == clone && lease.CheckChanges(MorphReadiness.CurrentMeshes(a)).Visibility); End();
var hair = new SkinnedMeshRenderer { name = "o_hair", sharedMesh = new Mesh { name = "hair" } };
ar.gameObject.Renderers = Array.Empty<SkinnedMeshRenderer>();
a.GameObject.Renderers = a.GameObject.Renderers.Concat(new[] { hair }).ToArray();
Check("New excluded head/hair meshes do not rebuild the torso", Begin(a) && !lease.CheckChanges(MorphReadiness.CurrentMeshes(a)).Mesh && PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA); End();
ar.bones[0] = new Transform();
Check("Native bone replacement restores only this actor at completion", Begin(a) && PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA + 1); End();
Check("Replaced skeleton is not repeatedly released", Begin(a) && PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA + 1); End();
var replacement = new Mesh { name = "o_lower_type04" }; ar.sharedMesh = replacement;
Check("Real mesh change is handled on the same native boundary", Begin(a) && PregnancyRuntime.HumanReleases.GetValueOrDefault(a.Pointer) == beforeA + 2); End();
lease.Dispose();
Check("Lease release never overwrites a native replacement", ar.sharedMesh == replacement);
Check("Old clone is retired through fluid lifetime protection", MeshLease.Owns(clone) && FluidMeshLifetime.Retired.Contains(clone));
a.GameObject.activeInHierarchy = false;
Check("Inactive actor cannot mutate", !Begin(a));
a.GameObject.activeInHierarchy = true;
Check("Reactivated actor is admitted without artificial samples", Begin(a)); End();
MorphReadiness.Forget(b); b.Disposed = true;
Check("Disposed actor cannot re-enter", !Begin(b));
Check("Other actors remain usable after disposal", Begin(a)); End();
ar.sharedMesh = null;
Check("Registered body with missing mesh remains rejected", !Begin(a));
ar.sharedMesh = replacement;
Check("Native repaired body resumes immediately", Begin(a)); End();
CompatibilityRegression.Run(Check, Character);
var shared = Character();
Check("Shared snapshot starts at its own actor boundary", Begin(shared));
var snapshot = MorphReadiness.CurrentMeshes(shared);
var sharedLease = new MeshLease(); int scans = shared.GameObject.Scans;
sharedLease.Acquire(shared, snapshot);
Check("Acquisition reuses boundary renderer selection", shared.GameObject.Scans == scans);
Check("Snapshot cannot be borrowed by another actor", MorphReadiness.CurrentMeshes(a) == null);
End();
Check("Leaving boundary drops renderer references", snapshot.Count == 0 && MorphReadiness.CurrentMeshes(shared) == null);
shared.GameObject.Scans = 0;
Check("Next native completion collects a fresh snapshot", Begin(shared));
var sharedChanges = sharedLease.CheckChanges(MorphReadiness.CurrentMeshes(shared));
Check("Readiness and lease checks perform exactly one hierarchy scan", shared.GameObject.Scans == 1 && !sharedChanges.Mesh && !sharedChanges.Visibility);
End();
shared.GameObject.Renderers[0].enabled = false;
shared.GameObject.Scans = 0; Begin(shared);
sharedChanges = sharedLease.CheckChanges(MorphReadiness.CurrentMeshes(shared));
Check("Visibility refresh uses the same single scan", sharedChanges.Visibility && !sharedChanges.Mesh && shared.GameObject.Scans == 1); End();
var garment = new SkinnedMeshRenderer { name = "shirt", sharedMesh = new Mesh { name = "shirt" } };
shared.GameObject.Renderers = shared.GameObject.Renderers.Concat(new[] { garment }).ToArray(); Begin(shared);
Check("New garment is detected within its completed update", sharedLease.CheckChanges(MorphReadiness.CurrentMeshes(shared)).Mesh); End();
sharedLease.Dispose(); Begin(shared); sharedLease.Acquire(shared, MorphReadiness.CurrentMeshes(shared)); End();
shared.GameObject.Renderers = shared.GameObject.Renderers.Where(r => r != garment).ToArray(); Begin(shared);
Check("Removed garment is detected without retaining a prior snapshot", sharedLease.CheckChanges(MorphReadiness.CurrentMeshes(shared)).Mesh); End();
sharedLease.Dispose(); MorphReadiness.Forget(shared);
Check("No test advanced a clock or frame", Time.frameCount == 0 && Time.unscaledTime == 0);
Console.WriteLine($"{checks} native completion checks passed (modeled adapter, not a Unity runtime test).");

namespace UnityEngine
{
    public class Object
    {
        private static int _id; public IntPtr Pointer { get; } = (IntPtr)(++_id); public int GetInstanceID() => (int)Pointer;
        public static Mesh Instantiate(Mesh m) => new() { name = m.name, vertexCount = m.vertexCount, subMeshCount = m.subMeshCount, blendShapeCount = m.blendShapeCount, isReadable = m.isReadable, bindposes = (int[])m.bindposes.Clone() };
    }
    [Flags] public enum HideFlags { None=0, DontUnloadUnusedAsset=32, DontSave=52 }
    public struct Bounds { }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public int Scans;
        public SkinnedMeshRenderer[] Renderers = Array.Empty<SkinnedMeshRenderer>();
        public T[] GetComponentsInChildren<T>(bool inactive) { Scans++; return Renderers.Cast<T>().ToArray(); }
    }
    public class Mesh : Object { public string name; public HideFlags hideFlags; public int vertexCount = 100, subMeshCount = 1, blendShapeCount; public bool isReadable = true; public int[] bindposes = new int[2]; }
    public class Transform : Object { }
    public class SkinnedMeshRenderer : Object { public string name; public Bounds localBounds; public Mesh sharedMesh; public bool enabled = true; public GameObject gameObject = new(); public Transform[] bones = new[] { new Transform(), new Transform() }; }
    public static class Time { public static float unscaledTime; public static int frameCount; }
}
namespace Il2CppSystem.Collections.Generic { public interface IReadOnlyCollection<T> { int Count { get; } } }
namespace Character
{
    public class BodyEntry { public SkinnedMeshRenderer Renderer; }
    public class HumanBody { public BodyEntry[] BodyRenderers = Array.Empty<BodyEntry>(); }
    public class Human : UnityEngine.Object
    {
        public bool Disposed, IsReloading; public bool HiPoly = true; public int Sex = 1;
        public GameObject GameObject = new(); public HumanBody Body = new();
        public static readonly RegistryList Registry = new(); public static RegistryList List => Registry;
        public void Load() { } public void Reload() { } public void Reload(ref int value) { }
        public void ReloadCoordinate() { } public void ReloadHead() { } public void ReloadHair() { }
        public void ReloadSkin() { } public void ReloadMannequin() { }
    }
    public class RegistryList : Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>
    {
        public readonly List<Human> Items = new(); public int Count => Items.Count; public Human this[int i] => Items[i];
        public T Cast<T>() where T : class => this as T;
    }
}
namespace Manager
{
    public static class Scene
    {
        public static bool IsNowLoading, IsNowLoadingFade;
        public static void LoadReserve() { } public static void LoadReserveAsync() { } public static void LoadStart() { }
    }
    public class MapManager { public static bool IsLoading; public void ChangeMapAsync() { } }
}
namespace AL.Scene { public class ActionMapScene { public static ActionMapScene Instance; public static bool Initialized; } }
namespace AL.H { public class HScene { public static HScene Instance; public bool IsDisposed, IsEnded, Loaded; } }
namespace HarmonyLib
{
    public class Harmony
    {
        public static int Hooks; public static bool PatchedByRef;
        public void Patch(System.Reflection.MethodInfo method, HarmonyMethod prefix) { Hooks++; PatchedByRef |= method.GetParameters().Any(p => p.ParameterType.IsByRef); }
    }
    public class HarmonyMethod { public HarmonyMethod(Type t, string n) { } }
    public static class AccessTools { public static List<System.Reflection.MethodInfo> GetDeclaredMethods(Type type) => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).ToList(); }
}
namespace ALPregnancy
{
    internal static class FluidMeshLifetime
    {
        internal static readonly List<Mesh> Retired = new();
        internal static void Retire(Mesh clone, Mesh original) => Retired.Add(clone);
        internal static void Collect(bool force = false) { }
    }
    internal static class BodyMeshSelection
    {
        internal static readonly string[] PelvisNames = { "waist" }, SpineNames = { "spine" };
        internal static int FindBone(Transform[] bones, string[] names) => names == PelvisNames ? 0 : 1;
    }
    internal static class PregnancyRuntime
    {
        internal static int Releases;
        internal static readonly Dictionary<IntPtr, int> HumanReleases = new();
        internal static void ReleaseHuman(Human h) => HumanReleases[h.Pointer] = HumanReleases.GetValueOrDefault(h.Pointer) + 1;
        internal static void ReleaseVisuals() => Releases++;
    }
    internal static class PregnancyController { internal static int Releases; internal static void SuspendHuman(Human h) { } internal static void SuspendForLoading() => Releases++; }
    internal sealed class Flag { public bool Value = false; }
    internal static class PregnancyPlugin { internal static readonly Log Logger = new(); internal static readonly Flag ConfigLog = new(); }
    internal class Log
    {
        internal readonly List<string> Infos = new(), Warnings = new();
        internal void LogInfo(string s) => Infos.Add(s);
        internal void LogWarning(string s) => Warnings.Add(s);
        internal void LogError(string s) => Console.WriteLine(s);
    }
}

