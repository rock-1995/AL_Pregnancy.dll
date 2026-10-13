using NV=System.Numerics.Vector3;
using NM=System.Numerics.Matrix4x4;
using NQ=System.Numerics.Quaternion;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static implicit operator NV(Vector3 v)=>new(v.x,v.y,v.z);
        public static implicit operator Vector3(NV v)=>new(v.X,v.Y,v.Z);
        public static implicit operator Vector4(Vector3 v)=>new(v.x,v.y,v.z,0);
        public static Vector3 zero=>default;
        public static Vector3 right=>new(1,0,0);
        public static Vector3 up=>new(0,1,0);
        public static Vector3 forward=>new(0,0,1);
        public float magnitude=>((NV)this).Length();
        public float sqrMagnitude=>((NV)this).LengthSquared();
        public Vector3 normalized=>sqrMagnitude>1e-12f?NV.Normalize(this):default;
        public void Normalize(){this=normalized;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>(NV)a+(NV)b;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>(NV)a-(NV)b;
        public static Vector3 operator -(Vector3 a)=>-(NV)a;
        public static Vector3 operator *(Vector3 a,float t)=>(NV)a*t;
        public static Vector3 operator *(float t,Vector3 a)=>a*t;
        public static Vector3 operator /(Vector3 a,float t)=>(NV)a/t;
        public static Vector3 Cross(Vector3 a,Vector3 b)=>NV.Cross(a,b);
        public static float Dot(Vector3 a,Vector3 b)=>NV.Dot(a,b);
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>NV.Lerp(a,b,Math.Clamp(t,0,1));
        public static float Distance(Vector3 a,Vector3 b)=>NV.Distance(a,b);
    }
    public struct Vector4
    {public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
    public struct Quaternion
    {
        public float x,y,z,w;
        public static implicit operator NQ(Quaternion v)=>new(v.x,v.y,v.z,v.w);
        public static implicit operator Quaternion(NQ v)=>new(){x=v.X,y=v.Y,z=v.Z,w=v.W};
        public static Quaternion identity=>NQ.Identity;
        public static Quaternion Inverse(Quaternion q)=>NQ.Inverse(q);
        public static Quaternion operator *(Quaternion a,Quaternion b)=>(NQ)a*(NQ)b;
        public static Vector3 operator *(Quaternion a,Vector3 v)=>NV.Transform(v,a);
        public static Quaternion LookRotation(Vector3 f,Vector3 up)
        {var z=f.normalized;var x=Vector3.Cross(up,z).normalized;var y=Vector3.Cross(z,x);return NQ.CreateFromRotationMatrix(new NM(x.x,x.y,x.z,0,y.x,y.y,y.z,0,z.x,z.y,z.z,0,0,0,0,1));}
    }
    public partial struct Matrix4x4
    {
        public NM Rows;
        public static Matrix4x4 identity=>new(){Rows=NM.Identity};
        public Matrix4x4 inverse{get{NM.Invert(Rows,out var result);return new(){Rows=result};}}
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new(){Rows=b.Rows*a.Rows};
        public Vector3 MultiplyPoint3x4(Vector3 v)=>NV.Transform(v,Rows);
        public Vector3 MultiplyVector(Vector3 v)=>NV.TransformNormal(v,Rows);
        public void SetColumn(int c,Vector4 v){this[0,c]=v.x;this[1,c]=v.y;this[2,c]=v.z;this[3,c]=v.w;}
    }
    public static class Mathf
    {
        public static float Clamp01(float v)=>Math.Clamp(v,0,1);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Max(params float[] a)=>a.Max();
        public static float Abs(float v)=>Math.Abs(v);
        public static float Sqrt(float v)=>MathF.Sqrt(v);
        public static float Pow(float value,float power)=>MathF.Pow(value,power);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static int FloorToInt(float v)=>(int)MathF.Floor(v);
        public static int CeilToInt(float v)=>(int)MathF.Ceiling(v);
        public static int RoundToInt(float v)=>(int)MathF.Round(v);
    }
    public class MonoBehaviour {public GameObject gameObject;}
    public class Object
    {
        public static void DestroyImmediate(MonoBehaviour component)
        {component.GetType().GetMethod("OnDestroy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.Invoke(component,null);component.gameObject.Components.Remove(component.GetType());}
    }
    public class GameObject
    {
        public string name="";
        public bool activeInHierarchy=true,activeSelf=true;
        internal Dictionary<System.Type,object> Components=new();
        public T GetComponent<T>() where T:class=>Components.TryGetValue(typeof(T),out var c)?(T)c:null;
        public T AddComponent<T>() where T:MonoBehaviour,new(){var c=new T{gameObject=this};Components[typeof(T)]=c;return c;}
    }
    public class Transform {public int GetInstanceID()=>GetHashCode();public string name;public Transform parent;public Vector3 localScale=new(1,1,1);public Matrix4x4 localToWorldMatrix=Matrix4x4.identity;public Matrix4x4 worldToLocalMatrix=>localToWorldMatrix.inverse;}
    public struct BoneWeight {public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;public float weight0,weight1,weight2,weight3;}
    public struct Bounds {public Vector3 center,size;public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}public void Expand(float n){size+=new Vector3(n,n,n);}}
    public enum SkinQuality {Auto,Bone1,Bone2,Bone4}
    public class Mesh
    {
        public string name;
        Vector3[] v=Array.Empty<Vector3>();
        public int VertexWrites;
        public Vector3[] vertices{get=>(Vector3[])v.Clone();set{v=(Vector3[])value.Clone();VertexWrites++;}}
        public Vector3[] normals=Array.Empty<Vector3>(); public Vector4[] tangents=Array.Empty<Vector4>(); public void RecalculateTangents(){}
        public BoneWeight[] boneWeights=Array.Empty<BoneWeight>();
        public Matrix4x4[] bindposes=Array.Empty<Matrix4x4>();
        public int[] triangles=Array.Empty<int>();
        public int[][] submeshes;
        public int subMeshCount=>submeshes==null?1:submeshes.Length;
        public int[] GetTriangles(int i)=>submeshes==null?triangles:submeshes[i];
        public int vertexCount=>v.Length;
        public int GetInstanceID()=>GetHashCode();
        public void RecalculateBounds(){}
        public void RecalculateNormals(){normals=new Vector3[v.Length];}
    }
    public class SkinnedMeshRenderer
    {
        public int GetInstanceID()=>GetHashCode();
        public string name=>sharedMesh.name;
        public Mesh sharedMesh;
        public Material[] sharedMaterials=Array.Empty<Material>();
        public Transform[] bones;
        public bool enabled=true;
        public GameObject gameObject=new();
        public Transform transform=new();
        public Transform rootBone;
        public Bounds bounds; public void ResetBounds(){} public Bounds localBounds=new(new Vector3(0,1,0),new Vector3(3,4,3));
        public SkinQuality quality=SkinQuality.Auto;
        public bool updateWhenOffscreen;
    }
    public class Material {public string name;}
}

