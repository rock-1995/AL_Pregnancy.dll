using System.Reflection;
namespace HarmonyLib
{
    public sealed class Harmony { public void Patch(MethodBase original, HarmonyMethod prefix=null, HarmonyMethod postfix=null) { if(original==null)throw new Exception("Missing native stub"); } }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type,string name) {} }
    public static class AccessTools
    {
        public static MethodInfo[] GetDeclaredMethods(Type type)=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
        public static MethodInfo Method(Type type,string name,Type[] args)=>type.GetMethod(name,args);
    }
}
namespace UnityEngine
{
    public class Object
    {
        private static int next;
        private readonly int id=++next;
        public IntPtr Pointer=>(IntPtr)id;
        public string name="";
        public bool Destroyed;
        public int GetInstanceID()=>id;
        public static bool operator ==(Object left,Object right)
        {
            bool a=ReferenceEquals(left,null) || left.Destroyed,b=ReferenceEquals(right,null) || right.Destroyed;
            return a || b ? a==b : ReferenceEquals(left,right);
        }
        public static bool operator !=(Object left,Object right)=>!(left==right);
        public override bool Equals(object other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>id;
    }
    public class Transform:Object { }
    public class GameObject:Object
    {
        public bool activeInHierarchy=true;
        public List<SkinnedMeshRenderer> Renderers=new();
        public static int HierarchyScans;
        public T[] GetComponentsInChildren<T>(bool inactive) { HierarchyScans++; return Renderers.Cast<T>().ToArray(); }
    }
    public class Mesh:Object
    {
        public int vertexCount=20,subMeshCount=1,blendShapeCount=0,bindposeCount=2;
        public bool isReadable=true;
    }
    public class SkinnedMeshRenderer:Object
    {
        private Mesh mesh;
        private Transform[] boneArray;
        public static int MeshReads,BoneReads;
        public Mesh sharedMesh { get { MeshReads++;return mesh; } set=>mesh=value; }
        public Transform[] bones { get { BoneReads++;return boneArray; } set=>boneArray=value; }
        public bool enabled=true;
        public GameObject gameObject=new();
    }
}
namespace Character
{
    public sealed class NativeArray<T>:List<T>
    {
        private static int next=10000;
        public readonly IntPtr Pointer=(IntPtr)(++next);
        public int Length=>Count;
    }
    public sealed class BodyRenderer { public UnityEngine.SkinnedMeshRenderer Renderer; }
    public sealed class HumanBody:UnityEngine.Object
    {
        public Human _human;
        public UnityEngine.GameObject _objBody=new();
        public UnityEngine.Transform _objBone=new();
        public NativeArray<BodyRenderer> BodyRenderers=new();
        public void Load(UnityEngine.Transform root,string name) {}
    }
    public sealed class Human:UnityEngine.Object
    {
        public bool Disposed,IsReloading,HiPoly=true;
        public byte Sex=1;
        public UnityEngine.GameObject GameObject=new();
        public HumanBody Body;
        public void Load() {} public void Reload() {} public void ReloadCoordinate() {} public void ReloadHead() {}
        public void ReloadHair() {} public void ReloadSkin() {} public void ReloadMannequin() {}
    }
}
namespace Manager
{
    public static class Scene { public static bool IsNowLoading; }
    public static class MapManager { public static bool IsLoading; }
}
namespace ALPregnancy
{
    public static class PregnancyPlugin { public static LogSink Logger=new(); }
    public sealed class LogSink { public void LogInfo(string message) {} public void LogWarning(string message) {} public void LogError(string message)=>throw new Exception(message); }
    public static class BodyChangeRuntime { public static void Release(Character.Human human) {} }
    public static class PregnancyRuntime { public static int Releases; public static void ReleaseMeshes(Character.Human human)=>Releases++; }
    public static class PregnancyController { public static int Suspensions; public static void SuspendHuman(Character.Human human)=>Suspensions++; }
    public static class BodyMeshSelection
    {
        public static readonly string[] PelvisNames={"pelvis"},SpineNames={"spine"};
        public static void Register(Character.Human human) {}
        public static int FindBone(UnityEngine.Transform[] bones,string[] names)=>Array.FindIndex(bones,b=>b!=null && names.Contains(b.name));
    }
    public static class TorsoSelectionPolicy
    {
        public static bool IsBodyPiece(bool registered,string renderer,string mesh)=>registered || renderer.Contains("body");
    }
    public static class BellyVertexMorph { public static bool IsClothingRenderer(UnityEngine.SkinnedMeshRenderer renderer)=>renderer.name.StartsWith("cloth"); }
    public static class MeshLease
    {
        public readonly record struct Observation(UnityEngine.SkinnedMeshRenderer Renderer,UnityEngine.Mesh Mesh,int RendererId,int MeshId,bool Active);
        public static readonly Dictionary<UnityEngine.Mesh,UnityEngine.Mesh> Sources=new();
        public static bool Relevant(UnityEngine.SkinnedMeshRenderer renderer,UnityEngine.Mesh mesh)=>!renderer.name.Contains("hair");
        public static UnityEngine.Mesh OriginalForReadiness(UnityEngine.Mesh mesh)=>mesh!=null && Sources.TryGetValue(mesh,out var original)?original:mesh;
    }
    [Flags] public enum NativeClothingChange { None=0,Visibility=1,Topology=2 }
    public static class NativeClothingChanges
    {
        private static readonly Dictionary<IntPtr,NativeClothingChange> pending=new();
        public static void Install(HarmonyLib.Harmony harmony) {}
        public static void MarkTopology(Character.Human human)=>Mark(human,NativeClothingChange.Topology|NativeClothingChange.Visibility);
        public static void Mark(Character.Human human,NativeClothingChange change)=>pending[human.Pointer]=pending.GetValueOrDefault(human.Pointer)|change;
        public static NativeClothingChange Consume(Character.Human human)=>pending.Remove(human.Pointer,out var change)?change:NativeClothingChange.None;
        public static void Forget(Character.Human human)=>pending.Remove(human.Pointer);
    }
}
