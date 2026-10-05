using System.Numerics;

namespace ALPregnancy;

internal static class SurfaceAttachment
{
    internal static Vector3 RotateOffset(Vector3 offset, Vector3 before, Vector3 after)
    {
        if (before.LengthSquared() < 1e-12f || after.LengthSquared() < 1e-12f) return offset;
        before = Vector3.Normalize(before); after = Vector3.Normalize(after);
        float dot = Math.Clamp(Vector3.Dot(before, after), -1, 1);
        if (dot > 0.9999999f) return offset;
        if (dot < -0.999999f)
        {
            Vector3 axis = Vector3.Cross(before, MathF.Abs(before.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            return Vector3.Transform(offset, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI));
        }
        return Vector3.Transform(offset, Quaternion.Normalize(new Quaternion(Vector3.Cross(before, after), 1f + dot)));
    }
}
