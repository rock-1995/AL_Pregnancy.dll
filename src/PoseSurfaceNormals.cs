using System.Numerics;
namespace ALPregnancy;

// Rotate authored normals/tangents by the change in the welded geometric normal.
// Only triangles incident to candidate vertices are evaluated per pose.
internal sealed class PoseSurfaceNormals
{
    private readonly int[] _groups, _triangles, _affected;
    private readonly Vector3[] _baseCross, _baseSum, _sum;
    internal PoseSurfaceNormals(Vector3[] vertices, int[] triangles, int[] groups, bool[] candidates)
    {
        _groups = groups; _triangles = triangles;
        int count = groups.Max() + 1;
        _baseCross = new Vector3[triangles.Length / 3]; _baseSum = new Vector3[count]; _sum = new Vector3[count];
        var affected = new List<int>();
        for (int t = 0; t < triangles.Length / 3; t++)
        {
            int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
            var cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            _baseCross[t] = cross; _baseSum[groups[a]] += cross; _baseSum[groups[b]] += cross; _baseSum[groups[c]] += cross;
            if (candidates[a] || candidates[b] || candidates[c]) affected.Add(t);
        }
        _affected = affected.ToArray();
    }
    internal void Apply(Vector3[] vertices, Vector3[] baseNormals, Vector4[] baseTangents, Vector3[] normals, Vector4[] tangents)
    {
        Array.Copy(_baseSum, _sum, _sum.Length);
        foreach (int t in _affected)
        {
            int a = _triangles[t * 3], b = _triangles[t * 3 + 1], c = _triangles[t * 3 + 2];
            var delta = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]) - _baseCross[t];
            _sum[_groups[a]] += delta; _sum[_groups[b]] += delta; _sum[_groups[c]] += delta;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            var before = _baseSum[_groups[i]]; var after = _sum[_groups[i]];
            if (Vector3.DistanceSquared(before, after) < 1e-16f || before.LengthSquared() < 1e-16f || after.LengthSquared() < 1e-16f)
            { normals[i] = baseNormals[i]; if (tangents.Length > 0) tangents[i] = baseTangents[i]; continue; }
            before = Vector3.Normalize(before); after = Vector3.Normalize(after);
            var axis = Vector3.Cross(before, after); float sine = axis.Length(), cosine = Math.Clamp(Vector3.Dot(before, after), -1, 1);
            Quaternion rotation = sine > 1e-8f ? Quaternion.CreateFromAxisAngle(axis / sine, MathF.Atan2(sine, cosine)) : Quaternion.Identity;
            normals[i] = Vector3.Normalize(Vector3.Transform(baseNormals[i], rotation));
            if (tangents.Length == 0) continue;
            var bt = baseTangents[i]; var tangent = Vector3.Transform(new Vector3(bt.X, bt.Y, bt.Z), rotation);
            tangent -= normals[i] * Vector3.Dot(tangent, normals[i]);
            if (tangent.LengthSquared() > 1e-10f) tangent = Vector3.Normalize(tangent);
            tangents[i] = new(tangent, bt.W);
        }
    }
}
