using System;
namespace UnityEngine;
internal static class Probe { internal static long Calls; internal static void Call()=>Calls++; }
public struct Vector3
{
 public float x,y,z;
 public Vector3(float x,float y,float z){Probe.Call();this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>new(0,0,0); public static Vector3 up=>new(0,1,0);
 public static Vector3 operator +(Vector3 a,Vector3 b){Probe.Call();return new(){x=a.x+b.x,y=a.y+b.y,z=a.z+b.z};}
 public static Vector3 operator -(Vector3 a,Vector3 b){Probe.Call();return new(){x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};}
 public static Vector3 operator *(Vector3 a,float s){Probe.Call();return new(){x=a.x*s,y=a.y*s,z=a.z*s};}
 public static Vector3 operator *(float s,Vector3 a){Probe.Call();return new(){x=s*a.x,y=s*a.y,z=s*a.z};}
 public static Vector3 operator /(Vector3 a,float s){Probe.Call();return new(){x=a.x/s,y=a.y/s,z=a.z/s};}
 public static Vector3 Cross(Vector3 a,Vector3 b){Probe.Call();return new(){x=a.y*b.z-a.z*b.y,y=a.z*b.x-a.x*b.z,z=a.x*b.y-a.y*b.x};}
 public static float Dot(Vector3 a,Vector3 b){Probe.Call();return a.x*b.x+a.y*b.y+a.z*b.z;}
 public float sqrMagnitude{get{Probe.Call();return x*x+y*y+z*z;}}
 public float magnitude{get{Probe.Call();return (float)Math.Sqrt(x*x+y*y+z*z);}}
 public Vector3 normalized {get{Probe.Call();float d=(float)Math.Sqrt(x*x+y*y+z*z);return d>1e-5f?new(){x=x/d,y=y/d,z=z/d}:default;}}
 public static Vector3 Lerp(Vector3 a,Vector3 b,float t){Probe.Call();if(t<0)t=0;else if(t>1)t=1;return new(){x=a.x+(b.x-a.x)*t,y=a.y+(b.y-a.y)*t,z=a.z+(b.z-a.z)*t};}
}
public struct Matrix4x4 { public float m00,m10,m20,m30,m01,m11,m21,m31,m02,m12,m22,m32,m03,m13,m23,m33; }
public struct Quaternion { public float x,y,z,w; }
public sealed class Mesh { public int[] triangles; }
public static class Mathf
{
 public static float Abs(float v){Probe.Call();return Math.Abs(v);}
 public static float Max(float a,float b){Probe.Call();return a>b?a:b;} public static int Max(int a,int b){Probe.Call();return a>b?a:b;}
 public static float Min(float a,float b){Probe.Call();return a<b?a:b;}
 public static float Sqrt(float v){Probe.Call();return (float)Math.Sqrt(v);}
 public static float Clamp01(float v){Probe.Call();if(v<0)v=0;else if(v>1)v=1;return v;}
 public static int RoundToInt(float v){Probe.Call();return (int)Math.Round(v);}
 public static int FloorToInt(float v){Probe.Call();return (int)Math.Floor(v);}
 public static int CeilToInt(float v){Probe.Call();return (int)Math.Ceiling(v);}
}
