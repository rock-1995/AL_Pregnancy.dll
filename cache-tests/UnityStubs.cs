namespace UnityEngine;

internal static class Probe
{
    internal static long Transforms, Distances;
    internal static int ThrowTransform, ThrowDistance;
    internal static void Reset() { Transforms=Distances=0; ThrowTransform=ThrowDistance=0; }
}
public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    public float sqrMagnitude
    {
        get { Probe.Distances++; if(Probe.ThrowDistance>0 && Probe.Distances==Probe.ThrowDistance) throw new InvalidOperationException("distance fixture failure"); return x*x+y*y+z*z; }
    }
}
public struct Matrix4x4
{
    public float m00,m10,m20,m30,m01,m11,m21,m31,m02,m12,m22,m32,m03,m13,m23,m33;
    public static Matrix4x4 identity=>new(){m00=1,m11=1,m22=1,m33=1};
    public Vector3 MultiplyPoint3x4(Vector3 point)
    {
        Probe.Transforms++;
        if(Probe.ThrowTransform>0 && Probe.Transforms==Probe.ThrowTransform) throw new InvalidOperationException("matrix fixture failure");
        return new(m00*point.x+m01*point.y+m02*point.z+m03,
                   m10*point.x+m11*point.y+m12*point.z+m13,
                   m20*point.x+m21*point.y+m22*point.z+m23);
    }
    // Test-only row/column access. Production helper uses the exact named fields.
    public float this[int i]
    {
        get=>i switch {0=>m00,1=>m01,2=>m02,3=>m03,4=>m10,5=>m11,6=>m12,7=>m13,8=>m20,9=>m21,10=>m22,11=>m23,12=>m30,13=>m31,14=>m32,15=>m33,_=>throw new ArgumentOutOfRangeException(nameof(i))};
        set {switch(i){case 0:m00=value;break;case 1:m01=value;break;case 2:m02=value;break;case 3:m03=value;break;case 4:m10=value;break;case 5:m11=value;break;case 6:m12=value;break;case 7:m13=value;break;case 8:m20=value;break;case 9:m21=value;break;case 10:m22=value;break;case 11:m23=value;break;case 12:m30=value;break;case 13:m31=value;break;case 14:m32=value;break;case 15:m33=value;break;default:throw new ArgumentOutOfRangeException(nameof(i));}}
    }
}
