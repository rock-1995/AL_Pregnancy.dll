using System.Numerics;

namespace ALPregnancy;

// A convex enclosure of the full native/virtual skin equation. Built on shape
// changes, then evaluated with bone matrices only; no per-frame vertex walk.
internal sealed class SkinBounds
{
    internal struct Box
    {
        public Vector3 Min, Max;
        public bool Valid;
        public void Include(Vector3 point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("Nonfinite skin bounds point.");
            if (!Valid) { Min = Max = point; Valid = true; }
            else { Min = Vector3.Min(Min, point); Max = Vector3.Max(Max, point); }
        }
        public void Include(Box box)
        { if (box.Valid) { Include(box.Min); Include(box.Max); } }
        public Box Transform(Matrix4x4 matrix)
        {
            if (!Valid) return default;
            var center = Vector3.Transform((Min + Max) * .5f, matrix);
            var e = (Max - Min) * .5f;
            var extent = new Vector3(
                MathF.Abs(matrix.M11)*e.X + MathF.Abs(matrix.M21)*e.Y + MathF.Abs(matrix.M31)*e.Z,
                MathF.Abs(matrix.M12)*e.X + MathF.Abs(matrix.M22)*e.Y + MathF.Abs(matrix.M32)*e.Z,
                MathF.Abs(matrix.M13)*e.X + MathF.Abs(matrix.M23)*e.Y + MathF.Abs(matrix.M33)*e.Z);
            var result = new Box(); result.Include(center - extent); result.Include(center + extent);
            return result;
        }
        public bool Contains(Vector3 point, float tolerance = 1e-5f) => Valid &&
            point.X >= Min.X-tolerance && point.Y >= Min.Y-tolerance && point.Z >= Min.Z-tolerance &&
            point.X <= Max.X+tolerance && point.Y <= Max.Y+tolerance && point.Z <= Max.Z+tolerance;
    }

    public Box[] Native;
    public Box Virtual;

    internal static SkinBounds Build(Vector3[] positions, VirtualWeights.Influence[][] weights, float[] alpha, int boneCount)
    {
        if (positions.Length != weights.Length || positions.Length != alpha.Length)
            throw new ArgumentException("Skin bounds arrays differ.");
        var result = new SkinBounds { Native = new Box[boneCount] };
        for (int i=0; i<positions.Length; i++)
        {
            if (alpha[i] > 0) result.Virtual.Include(positions[i]);
            if (alpha[i] >= 1) continue;
            foreach (var w in weights[i])
                if (w.Weight > 0) result.Native[w.Bone].Include(positions[i]);
        }
        return result;
    }

    internal Box Evaluate(Func<int,Matrix4x4> native, Matrix4x4 virtualMatrix)
    {
        var result = Virtual.Transform(virtualMatrix);
        for (int i=0; i<Native.Length; i++)
            if (Native[i].Valid) result.Include(Native[i].Transform(native(i)));
        return result;
    }
}
