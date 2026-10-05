using ALPregnancy;
using System.Numerics;

internal static class ShadingRegression
{
    internal static void Run(Action<string, bool> check)
    {
        Vector3 artist = Vector3.Normalize(new Vector3(0.1f, 0.15f, 1));
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, artist));
        SkinSurfaceShading.Part Triangle(Vector3[] positions, Vector3[] moved = null) => new()
        {
            Original = positions,
            Deformed = moved ?? positions.ToArray(),
            Normals = Enumerable.Repeat(artist, positions.Length).ToArray(),
            Tangents = Enumerable.Repeat(new Vector4(tangent, -1), positions.Length).ToArray(),
            Triangles = new[] { 0, 1, 2 }
        };
        Vector3[] lower = { new(0, 0, 0), new(1, 0, 0), new(1, 1, 0) };
        Vector3[] upper = { new(0, 0, 0), new(1, 1, 0), new(0, 1, 0) };
        var a = Triangle(lower);
        var b = Triangle(upper);
        var unchanged = SkinSurfaceShading.Apply(new[] { a, b });
        check("Split body with no deformation preserves authored normals exactly", unchanged.Parts.All(x => x.Normals.All(n => n == artist)));
        check("Reset preserves authored tangents and handedness exactly", unchanged.Parts.All(x => x.Tangents.All(t => t == new Vector4(tangent, -1))));
        check("Reset preserves original vertices exactly", unchanged.Parts[0].Vertices.SequenceEqual(lower) && unchanged.Parts[1].Vertices.SequenceEqual(upper));
        check("Separate body meshes share coincident seam groups", unchanged.CrossPartGroups == 2);

        var rotation = Matrix4x4.CreateRotationX(0.7f);
        a.Deformed = lower.Select(v => Vector3.Transform(v, rotation)).ToArray();
        b.Deformed = upper.Select(v => Vector3.Transform(v, rotation)).ToArray();
        var rotated = SkinSurfaceShading.Apply(new[] { a, b });
        Vector3 expected = Vector3.TransformNormal(artist, rotation);
        check("Surface tilt transports authored normal instead of replacing it", rotated.Parts.All(x => x.Normals.All(n => Vector3.Distance(n, expected) < 1e-5f)));
        check("Surface tilt transports authored tangents", rotated.Parts.All(x => x.Tangents.All(t => Vector3.Distance(new Vector3(t.X, t.Y, t.Z), Vector3.TransformNormal(tangent, rotation)) < 1e-5f)));
        check("Normal map tangent basis stays orthogonal with original handedness", rotated.Parts.All(x => x.Tangents.Select((t, i) => MathF.Abs(Vector3.Dot(new Vector3(t.X, t.Y, t.Z), x.Normals[i])) < 1e-5f && t.W == -1).All(v => v)));

        a = Triangle(lower, new[] { new Vector3(0, 0, 0.3f), new Vector3(1, 0, 0.2f), new Vector3(1, 1, 0.3f) });
        b = Triangle(upper, new[] { new Vector3(0, 0, 0.1f), new Vector3(1, 1, 0.1f), new Vector3(0, 1, -0.1f) });
        var seam = SkinSurfaceShading.Apply(new[] { a, b });
        check("Different upper/lower displacements cannot open shared seam", Vector3.Distance(seam.Parts[0].Vertices[0], seam.Parts[1].Vertices[0]) < 1e-6f && Vector3.Distance(seam.Parts[0].Vertices[2], seam.Parts[1].Vertices[1]) < 1e-6f);
        check("Shared seam gets identical normal rotation", Vector3.Distance(seam.Parts[0].Normals[0], seam.Parts[1].Normals[0]) < 1e-6f && Vector3.Distance(seam.Parts[0].Normals[2], seam.Parts[1].Normals[1]) < 1e-6f);
        check("Seam correction is measured for runtime diagnostics", MathF.Abs(seam.MaxSeamDelta - 0.1f) < 1e-5f);

        // Put the second part in a different, nonuniformly scaled rest mesh frame.
        var meshMap = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(3, 7, -2);
        Matrix4x4.Invert(meshMap, out var inverse);
        var mapped = new SkinSurfaceShading.Part
        {
            Original = b.Original.Select(v => Vector3.Transform(v, inverse)).ToArray(),
            Deformed = b.Deformed.Select(v => Vector3.Transform(v, inverse)).ToArray(),
            Normals = b.Normals.Select(n => Vector3.Normalize(Vector3.TransformNormal(n, Matrix4x4.Transpose(meshMap)))).ToArray(),
            Tangents = b.Tangents.Select(t => new Vector4(Vector3.Normalize(Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), inverse)), t.W)).ToArray(),
            Triangles = b.Triangles,
            ToReference = meshMap
        };
        var converted = SkinSurfaceShading.Apply(new[] { a, mapped });
        check("Different rest coordinate systems still weld across parts", converted.CrossPartGroups == 2 && Vector3.Distance(converted.Parts[0].Vertices[0], Vector3.Transform(converted.Parts[1].Vertices[0], meshMap)) < 1e-5f);
        check("Inverse-transpose normal conversion handles nonuniform scale", Vector3.Distance(converted.Parts[0].Normals[0], Vector3.Normalize(Vector3.TransformNormal(converted.Parts[1].Normals[0], Matrix4x4.Transpose(inverse)))) < 1e-5f);

        b = Triangle(upper);
        b.Normals = Enumerable.Repeat(Vector3.UnitX, 3).ToArray();
        var sharp = SkinSurfaceShading.Apply(new[] { Triangle(lower), b });
        check("Artist hard edges are not welded to smooth skin", sharp.CrossPartGroups == 0);
        b = Triangle(upper.Select(v => v + new Vector3(0, 0, 0.01f)).ToArray());
        var gap = SkinSurfaceShading.Apply(new[] { Triangle(lower), b });
        check("Nearby distinct surfaces are not pulled together", gap.CrossPartGroups == 0);
        var collapsed = Triangle(lower, Enumerable.Repeat(Vector3.Zero, 3).ToArray());
        var safe = SkinSurfaceShading.Apply(new[] { collapsed });
        check("Collapsed triangles never produce NaN normals", safe.Parts[0].Normals.All(n => float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z)));
        a = Triangle(lower, lower.Select(v => Vector3.Transform(v, Matrix4x4.CreateRotationX(MathF.PI))).ToArray());
        var reversed = SkinSurfaceShading.Apply(new[] { a });
        check("Opposite surface normals produce a finite unit tangent basis", reversed.Parts[0].Normals.All(n => float.IsFinite(n.X) && MathF.Abs(n.Length() - 1) < 1e-5f));
    }
}
