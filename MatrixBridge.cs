using UMatrix = UnityEngine.Matrix4x4;
using NMatrix = System.Numerics.Matrix4x4;

namespace ALPregnancy;

internal static class MatrixBridge
{
    internal static NMatrix ToManaged(UMatrix m) => new(
        m.m00, m.m10, m.m20, m.m30,
        m.m01, m.m11, m.m21, m.m31,
        m.m02, m.m12, m.m22, m.m32,
        m.m03, m.m13, m.m23, m.m33);

    internal static UMatrix ToUnity(NMatrix m) => new()
    {
        m00 = m.M11, m10 = m.M12, m20 = m.M13, m30 = m.M14,
        m01 = m.M21, m11 = m.M22, m21 = m.M23, m31 = m.M24,
        m02 = m.M31, m12 = m.M32, m22 = m.M33, m32 = m.M34,
        m03 = m.M41, m13 = m.M42, m23 = m.M43, m33 = m.M44
    };
}
