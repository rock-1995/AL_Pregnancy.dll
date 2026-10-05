using ALPregnancy;
using FluidFlow;
using UnityEngine;

int checks = 0;
void Check(string name, bool value) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
Mesh Clone() { var mesh = new Mesh(); MeshLease.Owned.Add(mesh.GetInstanceID()); return mesh; }
var original = new Mesh(); var clone = Clone(); var other = new Mesh();
var bodyRenderer = new SkinnedMeshRenderer();
var descriptors = new DescriptorArray { Masks = new[] { 3, 5 }, AtlasTransforms = new[] { 1f, 2f, 3f, 4f } };
var disabledCanvas = new FFCanvas { enabled = false };
disabledCanvas.Surfaces.Add(new Surface { Mesh = clone, Renderer = bodyRenderer, UVSet = 2, SubmeshDescriptors = descriptors });
disabledCanvas.Surfaces.Add(new Surface { Mesh = other, UVSet = 1 });
var secondCanvas = new FFCanvas(); secondCanvas.Surfaces.Add(new Surface { Mesh = clone });
FluidMeshLifetime.Retire(clone, original);
Check("Retirement alone leaves the cached mesh alive", !clone.Destroyed);
FluidMeshLifetime.Collect(true);
Check("Inactive scene-end canvas gets its mesh handed back", ReferenceEquals(disabledCanvas.Surfaces[0].Mesh, original));
Check("All canvases sharing the clone are repaired", ReferenceEquals(secondCanvas.Surfaces[0].Mesh, original));
Check("Clone is destroyed only after every cached reference is repaired", clone.Destroyed && !MeshLease.Owned.Contains(clone.GetInstanceID()));
Check("Renderer reference survives boxed Surface writeback", ReferenceEquals(disabledCanvas.Surfaces[0].Renderer, bodyRenderer));
Check("Texture mapping and unrelated surfaces remain unchanged", disabledCanvas.Surfaces[0].UVSet == 2 && ReferenceEquals(disabledCanvas.Surfaces[1].Mesh, other));
Check("Submesh masks and atlas transforms survive unchanged", ReferenceEquals(disabledCanvas.Surfaces[0].SubmeshDescriptors, descriptors) && descriptors.Masks.SequenceEqual(new[] { 3, 5 }) && descriptors.AtlasTransforms.SequenceEqual(new[] { 1f, 2f, 3f, 4f }));
Check("Native Object setter handles both matching boxed writebacks", SurfaceList.NativeWrites == 2);
Check("Broken generic value setter is never called", SurfaceList.GenericWrites == 0);
Check("The original shared asset is never destroyed", !original.Destroyed && !other.Destroyed);
FluidMeshLifetime.Collect(true);
Check("Repeated collection cannot destroy the same clone twice", clone.DestroyCalls == 1);

var held = Clone(); var holder = new SkinnedMeshRenderer { sharedMesh = held };
FluidMeshLifetime.Retire(held, original); FluidMeshLifetime.Collect(true);
Check("A live renderer prevents premature clone destruction", !held.Destroyed && MeshLease.Owned.Contains(held.GetInstanceID()));
holder.sharedMesh = original; FluidMeshLifetime.Collect(true);
Check("Deferred clone is freed once the renderer relinquishes it", held.Destroyed);

var missingOriginal = Clone(); var waitingCanvas = new FFCanvas(); waitingCanvas.Surfaces.Add(new Surface { Mesh = missingOriginal });
FluidMeshLifetime.Retire(missingOriginal, null); FluidMeshLifetime.Collect(true);
Check("Unavailable original cannot turn a cached surface into a null mesh", !missingOriginal.Destroyed && ReferenceEquals(waitingCanvas.Surfaces[0].Mesh, missingOriginal));
waitingCanvas.Surfaces.Clear(); FluidMeshLifetime.Collect(true);
Check("A cached-only mesh can retire after its canvas is uninitialized", missingOriginal.Destroyed);

var retry = Clone(); var retryCanvas = new FFCanvas(); retryCanvas.Surfaces.Add(new Surface { Mesh = retry });
FluidMeshLifetime.Retire(retry, original); Resources.FailType = typeof(SkinnedMeshRenderer);
FluidMeshLifetime.Collect(true);
Check("Scan failure defers destruction even after a partial handoff", !retry.Destroyed && MeshLease.Owned.Contains(retry.GetInstanceID()));
int warnings = PregnancyPlugin.Logger.Warnings;
FluidMeshLifetime.Collect(true);
Check("Repeated scan failures do not flood the log", PregnancyPlugin.Logger.Warnings == warnings);
Resources.FailType = null; FluidMeshLifetime.Collect(true);
Check("Collection retries successfully after transient failure", retry.Destroyed);
Check("No owned clone remains after all external users release it", MeshLease.Owned.Count == 0);
var parentClone = Clone(); var childClone = Clone(); var nestedCanvas = new FFCanvas();
nestedCanvas.Surfaces.Add(new Surface { Mesh = childClone });
FluidMeshLifetime.Retire(parentClone, original); FluidMeshLifetime.Retire(childClone, parentClone);
FluidMeshLifetime.Collect(true);
Check("Overlapping lease chains return directly to a surviving original", ReferenceEquals(nestedCanvas.Surfaces[0].Mesh, original));
Check("A clone-of-clone chain can retire safely in one pass", parentClone.Destroyed && childClone.Destroyed);
var cycleA = Clone(); var cycleB = Clone(); var cycleCanvas = new FFCanvas(); cycleCanvas.Surfaces.Add(new Surface { Mesh = cycleA });
FluidMeshLifetime.Retire(cycleA, cycleB); FluidMeshLifetime.Retire(cycleB, cycleA); FluidMeshLifetime.Collect(true);
Check("Invalid restoration cycle cannot destroy the cached mesh or loop forever", !cycleA.Destroyed);
cycleCanvas.Surfaces.Clear(); FluidMeshLifetime.Collect(true);
Check("Invalid chain is still collected once no canvas uses it", cycleA.Destroyed && cycleB.Destroyed);
var blocked = Clone(); var blockedCanvas = new FFCanvas();
blockedCanvas.Surfaces.Add(new Surface { Mesh = blocked, Renderer = bodyRenderer, UVSet = 1, SubmeshDescriptors = descriptors });
blockedCanvas.Surfaces.FailSet = true;
FluidMeshLifetime.Retire(blocked, original); FluidMeshLifetime.Collect(true);
Check("Native setter exception retains the still-referenced clone", !blocked.Destroyed && ReferenceEquals(blockedCanvas.Surfaces[0].Mesh, blocked));
blockedCanvas.Surfaces.FailSet = false; blockedCanvas.Surfaces.DropWrite = true;
FluidMeshLifetime.Collect(true);
Check("Readback detects a write that did not persist and prevents destruction", !blocked.Destroyed && MeshLease.Owned.Contains(blocked.GetInstanceID()));
blockedCanvas.Surfaces.DropWrite = false; FluidMeshLifetime.Collect(true);
Check("A deferred write retries and preserves all companion fields", blocked.Destroyed && ReferenceEquals(blockedCanvas.Surfaces[0].Renderer, bodyRenderer) && blockedCanvas.Surfaces[0].UVSet == 1 && ReferenceEquals(blockedCanvas.Surfaces[0].SubmeshDescriptors, descriptors));
Check("Final collection leaves no owned clones", MeshLease.Owned.Count == 0);
Check("No cleanup path called the unsafe generic setter", SurfaceList.GenericWrites == 0);
Console.WriteLine($"{checks} mesh lifetime checks passed (test doubles; not a Unity runtime test).");

// Test doubles model the observed ownership failure. The real plugin is separately
// compiled against the game's IL2CPP interfaces; this harness does not emulate Unity.
namespace UnityEngine
{
    public class Object : Il2CppSystem.Object
    {
        private static int _sequence;
        private readonly int _id = ++_sequence;
        public bool Destroyed; public int DestroyCalls;
        public Object() => Resources.Objects.Add(this);
        public int GetInstanceID() => _id;
        public static void Destroy(Object obj)
        {
            // Reproduce the crash precondition: destruction while a canvas caches the mesh.
            if (Resources.Objects.OfType<FFCanvas>().Any(x => x.Surfaces.Items.Any(s => ReferenceEquals(s.Mesh, obj))))
                throw new Exception("Attempt to destroy a mesh still cached by FluidFlow");
            obj.Destroyed = true; obj.DestroyCalls++;
        }
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => _id;
    }
    public class Mesh : Object { }
    public class SkinnedMeshRenderer : Object { public Mesh sharedMesh; }
    public static class Time { public static float unscaledTime => 0; }
    public static class Resources
    {
        public static readonly List<Object> Objects = new();
        public static Type FailType;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            if (FailType == typeof(T)) throw new Exception("Simulated object scan failure");
            return Objects.OfType<T>().Where(x => !x.Destroyed).ToArray();
        }
    }
}
namespace FluidFlow
{
    public class FFCanvas : UnityEngine.Object { public bool enabled = true; public SurfaceList Surfaces = new(); }
    public class DescriptorArray : Il2CppSystem.Object { public int[] Masks; public float[] AtlasTransforms; }
    public class Surface : Il2CppSystem.Object
    {
        public Mesh Mesh; public SkinnedMeshRenderer Renderer; public int UVSet; public DescriptorArray SubmeshDescriptors;
        public Surface Copy() => new() { Mesh = Mesh, Renderer = Renderer, UVSet = UVSet, SubmeshDescriptors = SubmeshDescriptors };
    }
    public class SurfaceList : Il2CppSystem.Collections.IList
    {
        public static int GenericWrites, NativeWrites;
        public bool FailSet, DropWrite;
        public readonly List<Surface> Items = new();
        public int Count => Items.Count;
        // IL2CPP's Surface value type is boxed on access: a writeback is needed.
        // The installed generated generic setter does not unbox this managed class.
        // Fail fast here instead of allowing the real game's native memory corruption.
        public Surface this[int i] { get => Items[i].Copy(); set { GenericWrites++; throw new InvalidOperationException("Unsafe generic Surface setter: boxed header would be copied as value data."); } }
        Il2CppSystem.Object Il2CppSystem.Collections.IList.this[int i]
        {
            get => Items[i].Copy();
            set
            {
                if (FailSet) throw new InvalidOperationException("Simulated native setter failure");
                NativeWrites++;
                if (!DropWrite) Items[i] = ((Surface)value).Copy();
            }
        }
        public T Cast<T>() where T : class => this as T ?? throw new InvalidCastException();
        public void Add(Surface surface) => Items.Add(surface);
        public void Clear() => Items.Clear();
    }
}
namespace Il2CppSystem
{
    public class Object
    {
        private static int _nextPointer;
        public IntPtr Pointer { get; } = (IntPtr)(++_nextPointer);
    }
}
namespace Il2CppSystem.Collections
{
    public interface IList { Il2CppSystem.Object this[int index] { get; set; } }
}
namespace ALPregnancy
{
    internal static class MeshLease
    {
        internal static readonly HashSet<int> Owned = new();
        internal static void ForgetOwnership(int id) => Owned.Remove(id);
    }
    internal static class PregnancyPlugin { internal static readonly Log Logger = new(); }
    internal class Log { internal int Warnings; internal void LogInfo(string value) { } internal void LogWarning(string value) => Warnings++; }
}
