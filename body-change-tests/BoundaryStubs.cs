// Only native Unity/interop and mesh inspection doubles. The two adapters under
// test (body scope + completed native boundary) are linked from production.
namespace UnityEngine
{
    public class Object { private static int _next; private readonly int _id=++_next; public int GetInstanceID()=>_id; }
    public class Transform : Object { }
    public class Mesh : Object { public string name="o_lower_type04"; public int vertexCount=100,subMeshCount=1,blendShapeCount; public bool isReadable=true; public int[] bindposes=new int[2]; }
    public class SkinnedMeshRenderer : Object { public string name="o_lower_type04"; public Mesh sharedMesh=new(); public bool enabled=true; public GameObject gameObject=new(); public Transform[] bones={new(),new()}; }
}
namespace Character { public class BodyEntry { public UnityEngine.SkinnedMeshRenderer Renderer; } }
namespace Manager { public static class Scene { public static bool IsNowLoading; } public static class MapManager { public static bool IsLoading; } }
namespace HarmonyLib
{
    public class Harmony
    {
        public readonly List<(System.Reflection.MethodInfo Method,HarmonyMethod Prefix,HarmonyMethod Finalizer)> Patches=new();
        public void Patch(System.Reflection.MethodInfo method,HarmonyMethod prefix,HarmonyMethod finalizer=null)=>Patches.Add((method,prefix,finalizer));
    }
    public class HarmonyMethod { public System.Reflection.MethodInfo Method; public HarmonyMethod(Type type,string name)=>Method=type.GetMethod(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic); }
    public static class AccessTools
    {
        public static List<System.Reflection.MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly).ToList();
        public static System.Reflection.MethodInfo Method(Type t,string name)=>t.GetMethod(name);
    }
}
namespace ALPregnancy
{
    internal static class MeshLease
    {
        internal readonly record struct Observation(UnityEngine.SkinnedMeshRenderer Renderer,UnityEngine.Mesh Mesh,int RendererId,int MeshId,bool Active);
        internal static bool Relevant(UnityEngine.SkinnedMeshRenderer renderer,UnityEngine.Mesh mesh)=>true;
        internal static UnityEngine.Mesh OriginalForReadiness(UnityEngine.Mesh mesh)=>mesh;
    }
    internal static class BodyMeshSelection
    {
        internal static readonly string[] PelvisNames={"waist"},SpineNames={"spine"};
        internal static int FindBone(UnityEngine.Transform[] bones,string[] names)=>names==PelvisNames?0:1;
    }
    internal static class TorsoSelectionPolicy { internal static bool IsBodyPiece(bool registered,string renderer,string mesh)=>registered; }
}
