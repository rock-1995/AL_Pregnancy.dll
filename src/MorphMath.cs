using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ALPregnancy;

// IL2CPP's generated Vector3/Mathf wrappers invoke native code and box results
// even for subtraction. Mesh loops use component arithmetic instead. Keep the
// Unity value types at the mesh boundary and the established rounding/order,
// clamped interpolation and 1e-5 normalization threshold.
internal static class MorphMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 V(float x,float y,float z) => new() { x=x,y=y,z=z };
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Add(Vector3 a,Vector3 b) => V(a.x+b.x,a.y+b.y,a.z+b.z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Subtract(Vector3 a,Vector3 b) => V(a.x-b.x,a.y-b.y,a.z-b.z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Multiply(Vector3 a,float s) => V(a.x*s,a.y*s,a.z*s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Multiply(float s,Vector3 a) => V(s*a.x,s*a.y,s*a.z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Divide(Vector3 a,float s) => V(a.x/s,a.y/s,a.z/s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Negate(Vector3 a) => V(-a.x,-a.y,-a.z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SqrMagnitude(Vector3 a) => a.x*a.x+a.y*a.y+a.z*a.z;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Magnitude(Vector3 a) => (float)Math.Sqrt(SqrMagnitude(a));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Normalize(Vector3 a) { float d=Magnitude(a);return d>1e-5f?Divide(a,d):default; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Cross(Vector3 a,Vector3 b) => V(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Lerp(Vector3 a,Vector3 b,float t) => LerpUnclamped(a,b,Clamp01(t));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 LerpUnclamped(Vector3 a,Vector3 b,float t) => V(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 MultiplyPoint3x4(Matrix4x4 m,Vector3 p) => V(
        m.m00*p.x+m.m01*p.y+m.m02*p.z+m.m03,
        m.m10*p.x+m.m11*p.y+m.m12*p.z+m.m13,
        m.m20*p.x+m.m21*p.y+m.m22*p.z+m.m23);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Min(float a,float b) => a<b?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Max(float a,float b) => a>b?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Min(int a,int b) => a<b?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Max(int a,int b) => a>b?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Abs(float a) => Math.Abs(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Clamp(float a,float min,float max) { if(a<min)a=min;else if(a>max)a=max;return a; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Clamp01(float a) => Clamp(a,0,1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Sqrt(float a) => (float)Math.Sqrt(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int FloorToInt(float a) => (int)Math.Floor(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CeilToInt(float a) => (int)Math.Ceiling(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int RoundToInt(float a) => (int)Math.Round(a);
}
