using System.Numerics;

namespace ALPregnancy;

// A material-coordinate patch, rebuilt with the static shape and evaluated in
// the current pelvis frame. No vertex IDs, animation names or frame delays.
internal sealed class UpperFoldSupport
{
    internal readonly record struct Surface(Vector3[] Material, int[] Triangles, int Priority = 0);
    internal readonly record struct Probe(int Surface, int A, int B, int C, Vector3 Bary, Vector3 Material)
    { internal bool Valid => Surface >= 0; }
    internal const int Rows = 33, Columns = 17;
    internal const float HalfAngle = 1.3962634f; // 80 degrees; the back is outside this patch.
    internal readonly Probe[] Probes = new Probe[Rows * Columns];
    internal readonly Vector3[] Before = new Vector3[Rows * Columns];
    internal readonly Vector3[] Delta = new Vector3[Rows * Columns];
    internal readonly float Low, High;
    private readonly TorsoProfile _torso;
    private readonly bool[] _valid = new bool[Columns];
    private readonly float[] _activation = new float[Columns], _filtered = new float[Columns];
    private readonly Vector2[] _curve = new Vector2[Rows];
    internal float MaxCorrection { get; private set; }
    internal int ActiveColumns { get; private set; }
    internal int ValidColumns => _valid.Count(x => x);

    internal UpperFoldSupport(TorsoProfile torso, IReadOnlyList<Surface> surfaces)
    {
        _torso = torso;
        Low = (torso.Navel * .48f + torso.Ribs * .52f) / torso.Span;
        High = torso.Ribs / torso.Span + .08f;
        for (int c = 0; c < Columns; c++)
        {
            var ray = Radial(c);
            _valid[c] = true;
            for (int row = 0; row < Rows; row++)
            {
                float y = Low + (High - Low) * row / (Rows - 1);
                var origin = new Vector3(0, y, torso.AxisAt(y * torso.Span) / torso.Span);
                var probe = new Probe(-1, 0, 0, 0, default, default);
                float best = 0; int priority = int.MaxValue;
                for (int s = 0; s < surfaces.Count; s++)
                {
                    var mesh = surfaces[s];
                    if (mesh.Priority > priority) continue;
                    for (int k = 0; k + 2 < mesh.Triangles.Length; k += 3)
                    {
                        int ia = mesh.Triangles[k], ib = mesh.Triangles[k + 1], ic = mesh.Triangles[k + 2];
                        var a = mesh.Material[ia]; var b = mesh.Material[ib]; var d = mesh.Material[ic];
                        if (y < MathF.Min(a.Y, MathF.Min(b.Y, d.Y)) || y > MathF.Max(a.Y, MathF.Max(b.Y, d.Y))) continue;
                        if (!Ray(origin, ray, a, b, d, out float radius, out var bary) || (mesh.Priority == priority && radius <= best) || radius > 1.2f) continue;
                        best = radius; priority = mesh.Priority;
                        probe = new(s, ia, ib, ic, bary, a * bary.X + b * bary.Y + d * bary.Z);
                    }
                }
                Probes[c * Rows + row] = probe;
                if (!probe.Valid) _valid[c] = false;
            }
        }
    }

    internal bool Contains(Vector3 material) => material.Y > Low && material.Y < High && MathF.Abs(Angle(material)) < HalfAngle;
    internal float Angle(Vector3 p) => MathF.Atan2(p.X, p.Z - _torso.AxisAt(p.Y * _torso.Span) / _torso.Span);
    private static Vector3 Radial(int column)
    {
        float a = -HalfAngle + 2 * HalfAngle * column / (Columns - 1);
        return new(MathF.Sin(a), 0, MathF.Cos(a));
    }
    internal static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * t * (t * (t * 6 - 15) + 10); }
    private static Vector2 Slice(Vector3 p, Vector3 radial) => new(p.Y, Vector3.Dot(p, radial));

    internal void Update(ReadOnlySpan<Vector3> posed, float strength)
    {
        Array.Clear(Delta, 0, Delta.Length); Array.Clear(_activation, 0, _activation.Length);
        MaxCorrection = 0; ActiveColumns = 0;
        posed.CopyTo(Before);
        strength = float.IsFinite(strength) ? Math.Clamp(strength, 0, 4) : 0;
        if (strength == 0) return;
        for (int c = 0; c < Columns; c++)
        {
            if (!_valid[c]) continue;
            var radial = Radial(c); int offset = c * Rows;
            for (int r = 0; r < Rows; r++) _curve[r] = Slice(posed[offset + r], radial);
            var chord = _curve[^1] - _curve[0]; float length = chord.Length();
            // A patch cannot safely bridge a chest folded below its lower anchor.
            // Leave that out-of-domain pose to the existing skinning.
            if (length < .08f || chord.X < .02f) continue;
            var along = chord / length; float reversal = 0;
            for (int r = 1; r < Rows; r++) reversal += MathF.Max(0, -Vector2.Dot(_curve[r] - _curve[r - 1], along));
            _activation[c] = Smooth((reversal - .002f) / .035f) * Smooth((length - .08f) / .05f) * Smooth((chord.X - .02f) / .04f);
        }
        // Spread the detected patch across nearby angular sectors without
        // diluting its centre. Quintic spatial falloff avoids a narrow ridge;
        // this contains no temporal state or animation delay.
        for (int c = 0; c < Columns; c++)
        {
            _filtered[c] = 0;
            for (int j = Math.Max(0,c-4); j <= Math.Min(Columns-1,c+4); j++)
                _filtered[c] = MathF.Max(_filtered[c], _activation[j] * Smooth(1-MathF.Abs(c-j)/4f));
        }
        for (int c = 0; c < Columns; c++)
        {
            if (!_valid[c]) continue;
            float side = Smooth((HalfAngle - MathF.Abs(-HalfAngle + 2 * HalfAngle * c / (Columns - 1))) / .4363323f);
            float amount = _filtered[c] * side * strength;
            if (amount <= 1e-6f) continue;
            var radial = Radial(c); int offset = c * Rows;
            for (int r = 0; r < Rows; r++) _curve[r] = Slice(posed[offset + r], radial);
            var start = _curve[0]; var chord = _curve[^1] - start; float length = chord.Length();
            if (length < .08f || chord.X < .02f) continue;
            amount *= Smooth((length - .08f) / .05f) * Smooth((chord.X - .02f) / .04f);
            var along = chord / length; var outward = new Vector2(-along.Y, along.X);
            float bulge = 0;
            for (int r = 1; r < Rows - 1; r++)
            {
                float t = r / (float)(Rows - 1), arch = 4 * t * (1 - t);
                float height = Vector2.Dot(_curve[r] - start, outward);
                float keepOutside = (_curve[r].Y - (start.Y + chord.Y * t)) / outward.Y;
                bulge = MathF.Max(bulge, MathF.Max(height, keepOutside) / arch);
            }
            bulge = Math.Clamp(bulge, 0, .18f);
            ActiveColumns++;
            for (int r = 1; r < Rows - 1; r++)
            {
                float t = r / (float)(Rows - 1);
                var target = start + chord * t + outward * (4 * bulge * t * (1 - t));
                var change = target - _curve[r]; change.Y = MathF.Max(0, change.Y);
                var delta = Vector3.UnitY * change.X + radial * change.Y;
                float fade = Smooth(t / .12f) * Smooth((1 - t) / .12f);
                if (delta.Length() > .25f) delta *= .25f / delta.Length();
                Delta[offset + r] = delta * (amount * fade);
                MaxCorrection = MathF.Max(MaxCorrection, Delta[offset + r].Length());
            }
        }
    }

    // The same field is used by body and garments. Garments retain their current
    // outward clearance; any already-negative gap gets a small positive margin.
    internal Vector3 Correction(Vector3 material, Vector3 current, bool cloth)
    {
        if (!Contains(material) || MaxCorrection <= 0) return Vector3.Zero;
        float angle = Angle(material), x = (angle + HalfAngle) / (2 * HalfAngle) * (Columns - 1);
        float y = (material.Y - Low) / (High - Low) * (Rows - 1);
        int c = Math.Clamp((int)x, 0, Columns - 2), r = Math.Clamp((int)y, 0, Rows - 2);
        if (!_valid[c] || !_valid[c + 1]) return Vector3.Zero;
        Vector3 Sample(Vector3[] values) => Vector3.Lerp(Vector3.Lerp(values[c * Rows + r], values[c * Rows + r + 1], y - r),
            Vector3.Lerp(values[(c + 1) * Rows + r], values[(c + 1) * Rows + r + 1], y - r), x - c);
        var delta = Sample(Delta);
        if (!cloth || delta.LengthSquared() < 1e-12f) return delta;
        var before = Sample(Before); var radial = new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle));
        float originalGap = Vector3.Dot(current - before, radial);
        float reach = 1 - Smooth((MathF.Max(0, originalGap) - .10f) / .35f);
        delta *= reach;
        float margin = .002f * Smooth(delta.Length() / .02f);
        float gap = Vector3.Dot(current + delta - (before + Sample(Delta)), radial);
        float extra = MathF.Max(0, MathF.Max(margin, originalGap) - gap) * reach;
        return delta + radial * MathF.Min(extra, .08f);
    }

    private static bool Ray(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 bary)
    {
        distance = 0; bary = default;
        var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(direction, e2); float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-10f) return false;
        var t = origin - a; float u = Vector3.Dot(t, p) / det;
        var q = Vector3.Cross(t, e1); float v = Vector3.Dot(direction, q) / det;
        if (u < -1e-5f || v < -1e-5f || u + v > 1.00001f) return false;
        distance = Vector3.Dot(e2, q) / det; bary = new(1 - u - v, u, v);
        return distance > 0;
    }
}
