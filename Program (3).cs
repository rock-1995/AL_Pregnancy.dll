using ALPregnancy;
using Character;
using FluidFlow;
using UnityEngine;
int checks=0;
void Check(string name,bool condition){if(!condition)throw new Exception(name);Console.WriteLine("PASS: "+name);checks++;}
(Human,SkinnedMeshRenderer,MeshLease) Lease(Mesh original)
{
    var h=new Human();var r=new SkinnedMeshRenderer{sharedMesh=original};h.GameObject.Renderers=new[]{r};
    var lease=new MeshLease();lease.Acquire(h);return(h,r,lease);
}
var original=new Mesh();var (human,renderer,lease)=Lease(original);var clone=renderer.sharedMesh;
Check("Acquisition installs a private live clone",clone!=original && MeshLease.Owns(clone));
Resources.UnloadUnusedAssets();
if(args.Contains("--reproduce"))
{
    Check("0.2.10 loses its source after native unused-asset cleanup",original==null);
    lease.Dispose();
    Check("0.2.10 restoration blanks the renderer",renderer.sharedMesh==null);
    Check("0.2.10 also destroys the last surviving clone",clone.Destroyed);
    Console.WriteLine("REPRODUCED: detached source collected -> null restore -> surviving clone destroyed.");
    return;
}
Check("Native cleanup cannot collect the detached original mesh",original!=null);
Check("Readiness resolves a live original after cleanup",MeshLease.OriginalForReadiness(clone)!=null);
lease.Dispose();
Check("Restore after native cleanup never installs a null mesh",renderer.sharedMesh!=null && renderer.sharedMesh==original);
Check("Final retirement releases our source protection",(original.hideFlags&HideFlags.DontUnloadUnusedAsset)==0);
Check("Retired private clone is actually destroyed",clone.Destroyed && !MeshLease.Owns(clone));

var shared=new Mesh{hideFlags=HideFlags.HideInInspector};var a=Lease(shared);var b=Lease(shared);
a.Item3.Dispose(); Resources.UnloadUnusedAssets();
Check("First of two users cannot drop the shared source hold",(shared.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0 && shared!=null);
shared.hideFlags|=HideFlags.NotEditable;
b.Item3.Dispose();
Check("Last user releases only the protection bit we added",shared.hideFlags==(HideFlags.HideInInspector|HideFlags.NotEditable));
var protectedSource=new Mesh{hideFlags=HideFlags.DontUnloadUnusedAsset|HideFlags.NotEditable};
var c=Lease(protectedSource);c.Item3.Dispose();
Check("Pre-existing source protection is never removed",protectedSource.hideFlags==(HideFlags.DontUnloadUnusedAsset|HideFlags.NotEditable));

var fluidOriginal=new Mesh();var fluid=Lease(fluidOriginal);var fluidClone=fluid.Item2.sharedMesh;
var canvas=new FFCanvas();canvas.Surfaces.Add(new Surface{Mesh=fluidClone,Renderer=fluid.Item2,UVSet=2});canvas.Surfaces.FailSet=true;
fluid.Item3.Dispose();fluid.Item2.sharedMesh=null;Resources.UnloadUnusedAssets();
Check("Deferred FluidFlow handoff retains the source hold",fluidOriginal!=null && !fluidClone.Destroyed);
canvas.Surfaces.FailSet=false;FluidMeshLifetime.Collect(true);
Check("FluidFlow handoff restores the original before releasing hold",canvas.Surfaces[0].Mesh==fluidOriginal && fluidClone.Destroyed && (fluidOriginal.hideFlags&HideFlags.DontUnloadUnusedAsset)==0);

var lost=new Mesh();var fallback=Lease(lost);var survivor=fallback.Item2.sharedMesh;
lost.Destroyed=true; // Explicit native Destroy/forced bundle unload ignores our flag.
fallback.Item3.Dispose();
Check("Explicit source destruction cannot blank a surviving renderer",fallback.Item2.sharedMesh==survivor && survivor!=null);
Check("Fallback baseline is no longer owned by a disposed lease",!MeshLease.Owns(survivor));
Check("Fallback remains a usable future baseline",MeshLease.OriginalForReadiness(survivor)==survivor);
Check("Fallback does not permanently pin an orphan mesh",(survivor.hideFlags&HideFlags.DontUnloadUnusedAsset)==0);
fallback.Item2.sharedMesh=null;Resources.UnloadUnusedAssets();
Check("Orphaned fallback can be reclaimed normally",survivor==null);

var replaced=Lease(new Mesh());var external=new Mesh();replaced.Item2.sharedMesh=external;replaced.Item3.Dispose();
Check("Disposal still respects a native replacement",replaced.Item2.sharedMesh==external);
var headHuman=new Human();var heads=new[]{"cf_O_hitomi_L","cf_O_namida_L","cf_O_canine"}.Select(n=>new SkinnedMeshRenderer{name=n,sharedMesh=new Mesh{name=n}}).ToArray();
headHuman.GameObject.Renderers=heads;var originals=heads.Select(r=>r.sharedMesh).ToArray();var headLease=new MeshLease();headLease.Acquire(headHuman);
Check("Eye tear and canine meshes stay under native ownership",headLease.ReadableCount==0 && heads.Select((r,i)=>r.sharedMesh==originals[i]).All(v=>v));
headLease.Dispose();
Check("Source lifecycle never uses the unsafe Surface setter",SurfaceList.GenericWrites==0);
Console.WriteLine($"{checks} source lifetime checks passed (native-root model; not a Unity runtime test).");

namespace UnityEngine
{
    public class Object : Il2CppSystem.Object
    {
        private static int _sequence;
        private readonly int _id = ++_sequence;
        public bool Destroyed; public int DestroyCalls;
        public HideFlags hideFlags;
        public static Mesh Instantiate(Mesh m) => new() { name=m.name, vertexCount=m.vertexCount, isReadable=m.isReadable };
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
    [Flags] public enum HideFlags { None=0, HideInHierarchy=1, HideInInspector=2, DontSaveInEditor=4, NotEditable=8, DontSaveInBuild=16, DontUnloadUnusedAsset=32, DontSave=52 }
    public class Mesh : Object { public string name="body"; public int vertexCount=100; public bool isReadable=true; }
    public struct Bounds { }
    public class GameObject : Object
    {
        public bool activeInHierarchy=true;
        public SkinnedMeshRenderer[] Renderers=Array.Empty<SkinnedMeshRenderer>();
        public T[] GetComponentsInChildren<T>(bool inactive)=>Renderers.Cast<T>().ToArray();
    }
    public class SkinnedMeshRenderer : Object
    {
        public string name="o_lower_type04"; public bool enabled=true;
        public GameObject gameObject=new(); public Bounds localBounds;
        public Mesh sharedMesh;
    }
    public static class Time { public static float unscaledTime => 0; }
    public static class Resources
    {
        public static readonly List<Object> Objects = new();
        public static Type FailType;
        // Model native Unity roots. CoreCLR-only Mesh wrappers are intentionally
        // not considered native asset roots, unlike renderer/canvas references.
        public static void UnloadUnusedAssets()
        {
            foreach(var mesh in Objects.OfType<Mesh>().Where(x=>!x.Destroyed).ToArray())
            {
                if((mesh.hideFlags & HideFlags.DontUnloadUnusedAsset)!=0)continue;
                bool used=Objects.OfType<SkinnedMeshRenderer>().Any(r=>!r.Destroyed && r.sharedMesh==mesh) ||
                    Objects.OfType<FFCanvas>().Any(c=>!c.Destroyed && c.Surfaces.Items.Any(s=>s.Mesh==mesh));
                if(!used)mesh.Destroyed=true;
            }
        }
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
    internal static class PregnancyPlugin { internal static readonly Log Logger = new(); internal static readonly Flag ConfigLog=new(); }
    internal class Log { internal int Warnings; internal void LogInfo(string value) { } internal void LogWarning(string value) => Warnings++; }
}

namespace Character { public class Human { public UnityEngine.GameObject GameObject=new(); } }
namespace ALPregnancy { internal class Flag { public bool Value=false; } }
