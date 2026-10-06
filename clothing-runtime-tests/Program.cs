using ALPregnancy;
using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

int checks=0;
void Check(string name,bool condition) { if(!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: "+name); }
foreach(var format in new[]{IndexFormat.UInt16,IndexFormat.UInt32})
{
    var source=Mesh.Fixture(format);
    var copy=ReadableClothingMesh.TryCreate(source,out var reason);
    Check("GPU-only mesh becomes a distinct readable copy: "+format,copy!=null && copy!=source && copy.isReadable && reason==null);
    Check("Position, normal, tangent, half UV and skin streams preserve bytes",source.Streams.Select((s,i)=>s.SequenceEqual(copy.Streams[i])).All(x=>x));
    Check("All submesh ranges/base vertices and index width preserved",copy.Indices.SequenceEqual(source.Indices) && copy.indexFormat==format && copy.Submeshes.SequenceEqual(source.Submeshes));
    Check("Binds, bounds and source ownership preserved",copy.bindposes.ToArray().SequenceEqual(source.bindposes.ToArray()) && copy.bounds==source.bounds && !source.isReadable && !source.Destroyed);
    Check("Every GPU handle disposed after a successful copy",GraphicsBuffer.Open==0);
}
void Failure(string label,Action<Mesh> corrupt)
{
    var source=Mesh.Fixture(IndexFormat.UInt16);corrupt(source);
    int before=UnityEngine.Object.DestroyedCount;
    var copy=ReadableClothingMesh.TryCreate(source,out var reason);
    Check(label+": rejected with diagnostic",copy==null && !string.IsNullOrEmpty(reason));
    Check(label+": no leaked buffer/source destruction",GraphicsBuffer.Open==0 && !source.Destroyed);
    if(source.blendShapeCount==0) Check(label+": partial copy destroyed",UnityEngine.Object.DestroyedCount==before+1);
}
Failure("Readback exception",m=>m.FailRead=true);
Failure("Zero-filled device data",m=>m.ZeroRead=true);
Failure("Truncated vertex buffer",m=>m.Streams[0]=new byte[4]);
Failure("Non-finite positions",m=>BitConverter.GetBytes(float.NaN).CopyTo(m.Streams[0],0));
Failure("Out-of-range bone index",m=>BitConverter.GetBytes(99).CopyTo(m.Streams[2],16));
Failure("Lost skin weights",m=>BitConverter.GetBytes(0f).CopyTo(m.Streams[2],0));
Failure("Unreadable blend shapes",m=>m.blendShapeCount=1);
Console.WriteLine($"{checks} clothing buffer checks passed (API doubles; native GPU runtime not exercised).");

namespace Il2CppSystem { public class Array { public System.Array Data; } }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T> : Il2CppSystem.Array
    {
        public Il2CppStructArray(int length) { Data=new T[length]; }
        public Il2CppStructArray(T[] data) { Data=data; }
        public int Length=>Data.Length;
        public T this[int i] { get=>(T)Data.GetValue(i);set=>Data.SetValue(value,i); }
        public U Cast<U>() where U:class => this as U;
        public T[] ToArray()=>(T[])Data.Clone();
    }
}
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16,UInt32 }
    public enum MeshUpdateFlags { Default,DontRecalculateBounds }
    public record struct SubMeshDescriptor(int indexStart,int indexCount,int baseVertex);
    public record struct VertexAttributeDescriptor(int stream,int stride);
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;public static int DestroyedCount;
        public static void Destroy(Object obj) { obj.Destroyed=true;DestroyedCount++; }
    }
    public enum HideFlags { DontSave }
    public record struct Bounds(int Tag);
    public record struct Matrix4x4(int Tag);
    public struct Vector3 { public float x,y,z; }
    public struct BoneWeight { public float weight0,weight1,weight2,weight3;public int boneIndex0,boneIndex1,boneIndex2,boneIndex3; }
    public class GraphicsBuffer
    {
        public static int Open;
        public int count,stride;
        byte[] data;bool fail,zero,disposed;
        public GraphicsBuffer(byte[] data,int stride,bool fail,bool zero) { this.data=data;this.stride=stride;count=data.Length/stride;this.fail=fail;this.zero=zero;Open++; }
        public void InternalGetData(Il2CppSystem.Array dest,int start,int gpuStart,int length,int elemSize)
        {
            if(fail)throw new Exception("Simulated GPU failure");
            if(!zero)System.Array.Copy(data,gpuStart*elemSize,dest.Data,start*elemSize,length*elemSize);
        }
        public void Dispose() { if(!disposed) { disposed=true;Open--; } }
    }
    public class Mesh : Object
    {
        public string name; public HideFlags hideFlags;public bool isReadable=true,FailRead,ZeroRead;
        public int vertexCount,blendShapeCount;public int vertexBufferCount=>Streams.Length;
        public IndexFormat indexFormat;public Bounds bounds;
        public Il2CppStructArray<Matrix4x4> bindposes;
        public byte[][] Streams=System.Array.Empty<byte[]>();public byte[] Indices;
        public int[] Strides;public SubMeshDescriptor[] Submeshes=System.Array.Empty<SubMeshDescriptor>();
        public int subMeshCount { get=>Submeshes.Length;set=>System.Array.Resize(ref Submeshes,value); }
        public Il2CppStructArray<VertexAttributeDescriptor> GetVertexAttributes()=>new(Strides.Select((s,i)=>new VertexAttributeDescriptor(i,s)).ToArray());
        public void SetVertexBufferParams(int count,Il2CppStructArray<VertexAttributeDescriptor> attrs)
        {
            vertexCount=count;Strides=attrs.ToArray().Select(a=>a.stride).ToArray();Streams=Strides.Select(s=>new byte[s*count]).ToArray();
        }
        public int GetVertexBufferStride(int stream)=>Strides[stream];
        public GraphicsBuffer GetVertexBuffer(int stream)=>new(Streams[stream],Strides[stream],FailRead,ZeroRead);
        public GraphicsBuffer GetIndexBuffer()=>new(Indices,indexFormat==IndexFormat.UInt32?4:2,FailRead,ZeroRead);
        public void InternalSetVertexBufferDataFromArray(int stream,Il2CppSystem.Array data,int start,int dest,int count,int elem,MeshUpdateFlags flags)=>System.Array.Copy(data.Data,start*elem,Streams[stream],dest*elem,count*elem);
        public void SetIndexBufferParams(int count,IndexFormat format) { indexFormat=format;Indices=new byte[count*(format==IndexFormat.UInt32?4:2)]; }
        public void InternalSetIndexBufferDataFromArray(Il2CppSystem.Array data,int start,int dest,int count,int elem,MeshUpdateFlags flags)=>System.Array.Copy(data.Data,start*elem,Indices,dest*elem,count*elem);
        public void SetSubMesh(int i,SubMeshDescriptor desc,MeshUpdateFlags flags)=>Submeshes[i]=desc;
        public SubMeshDescriptor GetSubMesh(int i)=>Submeshes[i];
        public Vector3[] vertices=>Enumerable.Range(0,vertexCount).Select(i=>new Vector3 {x=BitConverter.ToSingle(Streams[0],i*40),y=BitConverter.ToSingle(Streams[0],i*40+4),z=BitConverter.ToSingle(Streams[0],i*40+8)}).ToArray();
        public BoneWeight[] boneWeights=>Enumerable.Range(0,vertexCount).Select(i=>new BoneWeight {weight0=BitConverter.ToSingle(Streams[2],i*32),weight1=BitConverter.ToSingle(Streams[2],i*32+4),weight2=BitConverter.ToSingle(Streams[2],i*32+8),weight3=BitConverter.ToSingle(Streams[2],i*32+12),boneIndex0=BitConverter.ToInt32(Streams[2],i*32+16),boneIndex1=BitConverter.ToInt32(Streams[2],i*32+20),boneIndex2=BitConverter.ToInt32(Streams[2],i*32+24),boneIndex3=BitConverter.ToInt32(Streams[2],i*32+28)}).ToArray();
        public static Mesh Fixture(IndexFormat format)
        {
            var m=new Mesh {name="o_add_etc00_waist00",isReadable=false,vertexCount=6,Strides=new[]{40,4,32},indexFormat=format,bounds=new(10),bindposes=new(new[]{new Matrix4x4(17),new Matrix4x4(29)})};
            m.Streams=m.Strides.Select(s=>new byte[s*m.vertexCount]).ToArray();
            for(int i=0;i<m.vertexCount;i++)
            {
                for(int k=0;k<10;k++)BitConverter.GetBytes((i+k+1)*.1f).CopyTo(m.Streams[0],i*40+k*4);
                BitConverter.GetBytes((ushort)(0x3000+i)).CopyTo(m.Streams[1],i*4);
                BitConverter.GetBytes((ushort)(0x3400+i)).CopyTo(m.Streams[1],i*4+2);
                BitConverter.GetBytes(1f).CopyTo(m.Streams[2],i*32);
                BitConverter.GetBytes(i%2).CopyTo(m.Streams[2],i*32+16);
            }
            m.Indices=format==IndexFormat.UInt16 ? new ushort[]{0,1,2,0,1,2}.SelectMany(BitConverter.GetBytes).ToArray() : new uint[]{0,1,2,0,1,2}.SelectMany(BitConverter.GetBytes).ToArray();
            m.Submeshes=new[]{new SubMeshDescriptor(0,3,0),new SubMeshDescriptor(3,3,3)};
            return m;
        }
    }
}
