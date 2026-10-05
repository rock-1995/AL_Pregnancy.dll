using NMatrix=System.Numerics.Matrix4x4;
using NVector=System.Numerics.Vector3;
#pragma warning disable CS0649
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>
    {
        static long serial;public IntPtr Pointer=new(++serial);readonly T[] items;
        public Il2CppReferenceArray(T[] values){items=(T[])values.Clone();}
        public int Length=>items.Length;public T this[int i]{get=>items[i];set=>items[i]=value;}
    }
}
namespace Il2CppSystem.Collections.Generic {public class List<T>:System.Collections.Generic.List<T>{}}
namespace UnityEngine
{
    public class Object
    {
        static int serial;int id=++serial;public string name="test";public IntPtr Pointer=>new(id);
        public int GetInstanceID()=>id;public T TryCast<T>() where T:class=>this as T;
    }
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public struct Matrix4x4 {public NMatrix Value;public static Matrix4x4 identity=>new(){Value=NMatrix.Identity};}
    public class Transform:Object {public Matrix4x4 localToWorldMatrix=Matrix4x4.identity;}
    public class GameObject:Object {public bool activeInHierarchy=true;public object[] Components=Array.Empty<object>();public T[] GetComponentsInChildren<T>(bool all)=>Components.OfType<T>().ToArray();}
    public class Component:Object {public GameObject gameObject=new();public Transform transform=new();public bool enabled=true;}
    public class Mesh:Object
    {
        Vector3[] v=Array.Empty<Vector3>();public int VertexWrites;public Vector3[] vertices{get=>(Vector3[])v.Clone();set{v=(Vector3[])value.Clone();VertexWrites++;}}
        public bool isReadable=true;public int[] triangles=Array.Empty<int>();public Matrix4x4[] bindposes=Array.Empty<Matrix4x4>();public BoneWeight[] boneWeights=Array.Empty<BoneWeight>();
    }
    public struct BoneWeight {public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;public float weight0,weight1,weight2,weight3;}
    public class SkinnedMeshRenderer:Component {public Mesh sharedMesh;public Transform[] bones=Array.Empty<Transform>();}
    public class MeshCollider:Component {Mesh mesh;public int Assignments;public Mesh sharedMesh{get=>mesh;set{mesh=value;Assignments++;}}}
    public static class Resources {public static T[] FindObjectsOfTypeAll<T>()=>Array.Empty<T>();}
}
public class SkinnedCollisionHelper:UnityEngine.Component
{
    public struct VertexWeight {public int Index;public UnityEngine.Vector3 LocalPosition,LocalNormal;public float Weight;}
    public class WeightList {public UnityEngine.Transform Transform;public Il2CppSystem.Collections.Generic.List<VertexWeight> Weights;}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<WeightList> _nodeWeights;
    public bool _isInit=true;public UnityEngine.SkinnedMeshRenderer _skinnedMeshRenderer;public UnityEngine.MeshCollider _meshCollider;public UnityEngine.Mesh _meshCalc;
    public UnityEngine.Vector3[] _newVert;public Func<SkinnedCollisionHelper,bool> Before;public Action<SkinnedCollisionHelper> After;
    public int NativeCalls,Recooks;
    // Mirrors 0x32C7DF0: zero buffer, accumulate bone-local cached positions,
    // transform world to helper local, write vertices once, toggle collider once.
    public void UpdateCollisionMesh(bool release=true)
    {
        if(Before?.Invoke(this)==false){After?.Invoke(this);return;}
        NativeCalls++;var world=new NVector[_newVert.Length];
        for(int g=0;g<_nodeWeights.Length;g++)
        {var group=_nodeWeights[g];foreach(var v in group.Weights)world[v.Index]+=NVector.Transform(new(v.LocalPosition.x,v.LocalPosition.y,v.LocalPosition.z),group.Transform.localToWorldMatrix.Value)*v.Weight;}
        NMatrix.Invert(transform.localToWorldMatrix.Value,out var inverse);
        for(int i=0;i<world.Length;i++){var p=NVector.Transform(world[i],inverse);_newVert[i]=new(p.X,p.Y,p.Z);}
        _meshCalc.vertices=_newVert;_meshCollider.enabled=false;_meshCollider.enabled=true;Recooks++;After?.Invoke(this);
    }
    public bool Init()=>true;public bool Release()=>true;
}
namespace Obi
{
    public class ObiShapeTracker:UnityEngine.Object {public UnityEngine.Component collider;}
    public class ObiTriangleMeshHandle
    {
        public UnityEngine.Mesh owner; public int index=-1;
    }
    public class ObiColliderHandle {public ObiColliderBase owner;}
    public class ObiColliderBase:UnityEngine.Component
    {
        public ObiShapeTracker Tracker;public bool NeedsUpdate=true;
    }
    public class ObiMeshShapeTracker:ObiShapeTracker
    {
        public ObiTriangleMeshHandle handle;public int Invalidations;public bool Fail;public UnityEngine.Vector3[] Cached;
        public ObiColliderWorld World;public int PublishedIndex=-1;
        // Native 0x39BB770 destroys the mesh; 0x39CD7D3 shifts all later handles.
        // Collider shape dataIndex is a COPY and is not repaired by destruction.
        public void UpdateMeshData(){if(Fail)throw new Exception("cache busy");Invalidations++;World.Destroy(handle);}
        public void UpdateIfNeeded()
        {
            if(handle==null||handle.index<0)handle=World.GetOrCreate(((UnityEngine.MeshCollider)collider).sharedMesh);
            PublishedIndex=handle.index;
        }
        public void Read()=>Cached=PublishedIndex>=0&&PublishedIndex<World.Meshes.Count?World.Meshes[PublishedIndex].owner.vertices:null;
        public bool Correct=>PublishedIndex>=0&&PublishedIndex<World.Meshes.Count&&World.Meshes[PublishedIndex].owner==((UnityEngine.MeshCollider)collider).sharedMesh;
    }
    public class ObiColliderWorld
    {
        public static ObiColliderWorld instance=new();
        public Il2CppSystem.Collections.Generic.List<ObiColliderHandle> colliderHandles=new();
        public readonly List<ObiTriangleMeshHandle> Meshes=new();public bool Dirty=true;
        public ObiTriangleMeshHandle GetOrCreate(UnityEngine.Mesh mesh)
        {
            var found=Meshes.FirstOrDefault(h=>h.owner==mesh);if(found!=null)return found;
            var h=new ObiTriangleMeshHandle{owner=mesh,index=Meshes.Count};Meshes.Add(h);return h;
        }
        public void Destroy(ObiTriangleMeshHandle h)
        {
            if(h==null||h.index<0)return;Meshes.RemoveAt(h.index);h.index=-1;
            for(int i=0;i<Meshes.Count;i++)Meshes[i].index=i;
        }
        public ObiMeshShapeTracker Register(UnityEngine.MeshCollider collider,bool initialize=true)
        {
            var tracker=new ObiMeshShapeTracker{collider=collider,World=this};
            colliderHandles.Add(new(){owner=new ObiColliderBase{Tracker=tracker}});
            if(initialize)tracker.UpdateIfNeeded();return tracker;
        }
        public void MarkColliderAsNeedingUpdate(ObiColliderHandle handle)
        {
            handle.owner.NeedsUpdate=true;
            // Native mark moves handles into the update partition; enumeration is unsafe.
            colliderHandles.Remove(handle);colliderHandles.Insert(0,handle);
        }
        public void SetDirty()=>Dirty=true;
        public void UpdateWorld(float deltaTime=0,bool updateDynamics=true)
        {
            if(!Dirty)return;Dirty=false;
            foreach(var h in colliderHandles.ToArray())if(h.owner.NeedsUpdate)
            {((ObiMeshShapeTracker)h.owner.Tracker).UpdateIfNeeded();h.owner.NeedsUpdate=false;}
        }
    }
    public class ObiCollider:UnityEngine.Component {public ObiShapeTracker Tracker;}
}
namespace Character {public class Human {public IntPtr Pointer;public UnityEngine.GameObject GameObject=new();public HumanBody Body=new();}public class HumanBody{public UnityEngine.GameObject objHitBody;}}
namespace HarmonyLib
{
    public class Harmony {public void Patch(object method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}}
    public class HarmonyMethod {public int priority;public HarmonyMethod(Type type,string name){}}
    public static class AccessTools {public static object Method(Type type,string name)=>null;}
    public static class Priority {public const int Last=0,First=100;}
}
namespace ALPregnancy
{
    public static class MatrixBridge {public static NMatrix ToManaged(UnityEngine.Matrix4x4 m)=>m.Value;}
    internal static class BellyDeformSettings {public static VtxSettings Vtx=new();}
    internal static partial class BellyVertexMorph
    {
        static readonly Dictionary<int,CharaState> _state=new();static readonly Dictionary<int,List<MeshRecord>> _records=new();
        static class Log {public static int Errors;public static void LogInfo(string s)=>Console.WriteLine(s);public static void LogWarning(string s)=>Console.WriteLine(s);public static void LogError(string s){Errors++;Console.WriteLine(s);}}
        class CharaState {public CollisionRig Collision;public TorsoProfile Profile;public float LastAppliedRate=1;public NMatrix Frame=NMatrix.Identity;public VirtualRig Virtual;public UnityEngine.SkinnedMeshRenderer SMR;public IntPtr HumanPtr;}
        class MeshRecord {public VirtualBinding Virtual;public UnityEngine.Mesh Mesh;}
        class VirtualBinding {public UnityEngine.Transform[] Bones;public UnityEngine.Matrix4x4[] Binds;public UnityEngine.BoneWeight[] Weights;public NMatrix[] NativeMatrices;public int[] NativeTicks;public int Tick;}
        class VirtualRig {public bool Failed;public FoldRig Fold;public NMatrix PelvisBind=NMatrix.Identity;public UnityEngine.Transform Pelvis=new();public VirtualAxisMath.Result Last;}
        class FoldRig {public bool Failed;public NMatrix Frame=NMatrix.Identity;public UpperFoldSupport Field;}
        static NMatrix FrameMatrix(NMatrix frame)=>frame;
        static NVector[] ToManagedVectors(UnityEngine.Vector3[] v)=>v.Select(p=>new NVector(p.x,p.y,p.z)).ToArray();
        static UnityEngine.Vector3 ToUnity(NVector p)=>new(p.X,p.Y,p.Z);
        static MeshRecord FindRecord(List<MeshRecord> records,UnityEngine.Mesh mesh)=>records.FirstOrDefault(r=>r.Mesh==mesh);
        static IEnumerable<(int Bone,float Weight)> Weights(UnityEngine.BoneWeight w)=>new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)};
        static float[] ComputeBreastWeights(UnityEngine.Mesh m,int n,UnityEngine.SkinnedMeshRenderer r)=>m.boneWeights.Select(w=>Weights(w).Where(x=>x.Weight>0&&new[]{"mune","breast","bust","chichi","nipple"}.Any(k=>r.bones[x.Bone].name.Contains(k,StringComparison.OrdinalIgnoreCase))).Sum(x=>x.Weight)).ToArray();
        static bool IsLegBoneName(string name){var s=name.ToLowerInvariant();return !new[]{"hipleg","hip_leg","hip-leg","hip leg"}.Any(s.Contains)&&(s.Contains("thigh")||(s.Contains("leg")&&!s.Contains("spine"))||s.Contains("knee"));}
        static float[] ComputeBellyInfluence(UnityEngine.Mesh m,int n,HashSet<int> belly,HashSet<int> leg)=>m.boneWeights.Select(w=>BellyShape.BoneInfluence(Weights(w).Where(x=>belly.Contains(x.Bone)).Sum(x=>x.Weight),Weights(w).Where(x=>leg.Contains(x.Bone)).Sum(x=>x.Weight))).ToArray();
        static int[] ComputeNormalWeldGroup(UnityEngine.Vector3[] v){var d=new Dictionary<UnityEngine.Vector3,int>();return v.Select((p,i)=>{if(!d.TryGetValue(p,out int g))d[p]=g=i;return g;}).ToArray();}
        static NMatrix NativeMatrix(VirtualBinding b,int i)
        {if(b.NativeTicks[i]!=b.Tick){if(b.Bones[i]==null)throw new Exception("missing bone");b.NativeMatrices[i]=b.Binds[i].Value*b.Bones[i].localToWorldMatrix.Value;b.NativeTicks[i]=b.Tick;}return b.NativeMatrices[i];}
    }
}
