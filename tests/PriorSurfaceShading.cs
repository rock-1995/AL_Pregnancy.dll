// Frozen 0.1.12 reference for no-geometry-change regression.
using System.Numerics;

namespace ALPregnancy;

// Pure managed math: Unity 6's generated Matrix4x4 wrappers are not all valid IL.
// Transport artist-authored shading by the geometric surface rotation; do not
// replace it with triangle normals. Work on visible pieces in one rest space.
internal static class PriorSurfaceShading
{
    internal sealed class Part
    {
        public Vector3[] Original, Deformed, Normals;
        public Vector4[] Tangents;
        public int[] Triangles;
        public Matrix4x4 ToReference = Matrix4x4.Identity;
    }

    internal sealed class Output
    {
        public Vector3[] Vertices, Normals;
        public Vector4[] Tangents;
    }

    internal sealed class Result
    {
        public Output[] Parts;
        public int WeldGroups, CrossPartGroups;
        public float MaxSeamDelta;
    }

    private sealed class Group
    {
        public Vector3 Position, Normal, Displacement, Before, After;
        public int Count, FirstPart;
        public bool CrossPart;
    }

    internal static Result Apply(IReadOnlyList<Part> parts)
    {
        var result = new Result { Parts = new Output[parts.Count] };
        var originals = new Vector3[parts.Count][];
        var deformed = new Vector3[parts.Count][];
        var normals = new Vector3[parts.Count][];
        var inverse = new Matrix4x4[parts.Count];
        var groupsByVertex = new int[parts.Count][];
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            int n = part.Original.Length;
            if (part.Deformed.Length != n || part.Normals.Length != n ||
                !Matrix4x4.Invert(part.ToReference, out inverse[p]))
                throw new ArgumentException("Invalid skin surface or rest-space mapping.");
            originals[p] = new Vector3[n];
            deformed[p] = new Vector3[n];
            normals[p] = new Vector3[n];
            groupsByVertex[p] = new int[n];
            var normalMatrix = Matrix4x4.Transpose(inverse[p]);
            for (int i = 0; i < n; i++)
            {
                originals[p][i] = Vector3.Transform(part.Original[i], part.ToReference);
                deformed[p][i] = Vector3.Transform(part.Deformed[i], part.ToReference);
                normals[p][i] = Unit(Vector3.TransformNormal(part.Normals[i], normalMatrix), Vector3.UnitY);
                min = Vector3.Min(min, originals[p][i]);
                max = Vector3.Max(max, originals[p][i]);
            }
        }

        float epsilon = MathF.Max(1e-6f, Vector3.Distance(min, max) * 1e-5f);
        float epsilonSq = epsilon * epsilon;
        var buckets = new Dictionary<(int X, int Y, int Z), List<int>>();
        var groups = new List<Group>();
        for (int p = 0; p < parts.Count; p++)
        for (int i = 0; i < originals[p].Length; i++)
        {
            Vector3 point = originals[p][i], normal = normals[p][i];
            var cell = Cell(point, epsilon);
            int found = -1;
            // Neighbor cells avoid leaving a seam on quantization boundaries.
            for (int x = -1; x <= 1 && found < 0; x++)
            for (int y = -1; y <= 1 && found < 0; y++)
            for (int z = -1; z <= 1 && found < 0; z++)
                if (buckets.TryGetValue((cell.X + x, cell.Y + y, cell.Z + z), out var candidates))
                    foreach (int candidate in candidates)
                        if (Vector3.DistanceSquared(point, groups[candidate].Position) <= epsilonSq &&
                            Vector3.Dot(normal, groups[candidate].Normal) >= 0.98f)
                        { found = candidate; break; }
            if (found < 0)
            {
                found = groups.Count;
                groups.Add(new Group { Position = point, Normal = normal, FirstPart = p });
                if (!buckets.TryGetValue(cell, out var list)) buckets[cell] = list = new List<int>();
                list.Add(found);
            }
            Group group = groups[found];
            group.Count++;
            group.CrossPart |= group.FirstPart != p;
            group.Displacement += deformed[p][i] - point;
            groupsByVertex[p][i] = found;
        }

        foreach (Group group in groups)
        {
            group.Displacement /= group.Count;
            if (group.Count > 1) result.WeldGroups++;
            if (group.CrossPart) result.CrossPartGroups++;
        }
        for (int p = 0; p < parts.Count; p++)
        {
            for (int i = 0; i < originals[p].Length; i++)
            {
                Group group = groups[groupsByVertex[p][i]];
                if (group.Count < 2) continue;
                Vector3 next = originals[p][i] + group.Displacement;
                result.MaxSeamDelta = MathF.Max(result.MaxSeamDelta, Vector3.Distance(next, deformed[p][i]));
                deformed[p][i] = next;
            }
            int[] triangles = parts[p].Triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if ((uint)a >= originals[p].Length || (uint)b >= originals[p].Length || (uint)c >= originals[p].Length)
                    throw new ArgumentException("Invalid skin triangle index.");
                Vector3 before = Vector3.Cross(originals[p][b] - originals[p][a], originals[p][c] - originals[p][a]);
                Vector3 after = Vector3.Cross(deformed[p][b] - deformed[p][a], deformed[p][c] - deformed[p][a]);
                // Ignore triangles that collapse instead of feeding undefined directions into shading.
                if (before.LengthSquared() < 1e-20f || after.LengthSquared() < 1e-20f) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int i = triangles[t + corner];
                    Group group = groups[groupsByVertex[p][i]];
                    group.Before += before;
                    group.After += after;
                }
            }
        }

        var rotations = new Quaternion[groups.Count];
        for (int i = 0; i < groups.Count; i++) rotations[i] = Rotation(groups[i].Before, groups[i].After);
        for (int p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            var output = new Output
            {
                Vertices = (Vector3[])part.Deformed.Clone(),
                Normals = (Vector3[])part.Normals.Clone(),
                Tangents = part.Tangents == null ? null : (Vector4[])part.Tangents.Clone()
            };
            var backNormal = Matrix4x4.Transpose(part.ToReference);
            for (int i = 0; i < output.Vertices.Length; i++)
            {
                if (Vector3.DistanceSquared(deformed[p][i], Vector3.Transform(part.Deformed[i], part.ToReference)) > 1e-14f)
                    output.Vertices[i] = Vector3.Transform(deformed[p][i], inverse[p]);
                Quaternion rotation = rotations[groupsByVertex[p][i]];
                if (rotation == Quaternion.Identity) continue; // Exact original NT on unchanged areas.
                Vector3 normal = Unit(Vector3.TransformNormal(Vector3.Transform(normals[p][i], rotation), backNormal), part.Normals[i]);
                output.Normals[i] = normal;
                if (output.Tangents?.Length == output.Vertices.Length)
                {
                    Vector4 authored = part.Tangents[i];
                    Vector3 tangent = Vector3.TransformNormal(new Vector3(authored.X, authored.Y, authored.Z), part.ToReference);
                    tangent = Vector3.TransformNormal(Vector3.Transform(tangent, rotation), inverse[p]);
                    tangent = Unit(tangent - normal * Vector3.Dot(normal, tangent), new Vector3(authored.X, authored.Y, authored.Z));
                    output.Tangents[i] = new Vector4(tangent, authored.W);
                }
            }
            result.Parts[p] = output;
        }
        return result;
    }

    private static (int X, int Y, int Z) Cell(Vector3 p, float size)
        => ((int)MathF.Floor(p.X / size), (int)MathF.Floor(p.Y / size), (int)MathF.Floor(p.Z / size));

    private static Vector3 Unit(Vector3 value, Vector3 fallback)
        => value.LengthSquared() > 1e-20f ? Vector3.Normalize(value) : fallback;

    private static Quaternion Rotation(Vector3 before, Vector3 after)
    {
        if (before.LengthSquared() < 1e-20f || after.LengthSquared() < 1e-20f) return Quaternion.Identity;
        before = Vector3.Normalize(before);
        after = Vector3.Normalize(after);
        float dot = Math.Clamp(Vector3.Dot(before, after), -1f, 1f);
        if (dot > 0.9999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            Vector3 axis = Vector3.Cross(before, MathF.Abs(before.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(before, after), 1f + dot));
    }
}
