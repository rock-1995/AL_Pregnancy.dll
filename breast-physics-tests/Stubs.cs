namespace UnityEngine {
    public class Transform {
        public string name; public Transform parent; public int Id;
        public Vector3 localScale=new(1,1,1);
        public Matrix4x4 localToWorldMatrix=new(){Value=System.Numerics.Matrix4x4.Identity};
        public int GetInstanceID()=>Id;
    }
    public class GameObject { }
    public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
    public struct Matrix4x4 { public System.Numerics.Matrix4x4 Value; }
    public struct BoneWeight { public int boneIndex0,boneIndex1,boneIndex2,boneIndex3; public float weight0,weight1,weight2,weight3; }
    public class Mesh { public string name="test";public BoneWeight[] boneWeights; }
    public class SkinnedMeshRenderer { public Transform[] bones; }
    public static class Mathf {public static float Clamp01(float x)=>Math.Clamp(x,0,1);}
}
namespace ALPregnancy {
    internal static class MatrixBridge {internal static System.Numerics.Matrix4x4 ToManaged(UnityEngine.Matrix4x4 m)=>m.Value;}
    internal static partial class BellyVertexMorph {
        static void RuntimeLogInfo(string s)=>Console.WriteLine(s);
        static string PathOf(UnityEngine.Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        internal static float[] TestWeights(Character.Human h,UnityEngine.Mesh m,UnityEngine.SkinnedMeshRenderer r)=>ComputeBreastWeights(h,m,m.boneWeights.Length,r);
        internal static string[] TestPaths(Character.Human h)=>BreastPhysicsRootPaths(h);
    }
}
