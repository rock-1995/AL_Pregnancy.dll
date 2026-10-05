using System.Numerics;

namespace ALPregnancy;

// Rest-space correspondence only. Original low-poly topology, skinning and
// collision offsets remain native; sample the visible body's added displacement.
internal static class CollisionSurfaceMap
{
    internal sealed class Surface
    {
        internal Vector3[] Original;
        internal int[] Triangles;
        internal bool[] Affected;
        internal int Priority;
    }
    internal readonly record struct Probe(int Surface, int A, int B, int C, Vector3 Bary)
    {
        internal bool Valid => Surface >= 0;
        internal Vector3 Sample(Vector3[][] displacement) => !Valid ? Vector3.Zero :
            displacement[Surface][A] * Bary.X + displacement[Surface][B] * Bary.Y + displacement[Surface][C] * Bary.Z;
    }
    internal static Probe[] Build(Vector3[] points, bool[] excluded, Surface[] surfaces, float maxDistance)
    {
        var result = Enumerable.Repeat(new Probe(-1, 0, 0, 0, default), points.Length).ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            if (BreastExclusion.Contains(excluded, i)) continue;
            float best = maxDistance * maxDistance; int priority = int.MaxValue;
            for (int s = 0; s < surfaces.Length; s++)
            {
                var surface = surfaces[s]; var v = surface.Original; var tr = surface.Triangles;
                for (int t = 0; t + 2 < tr.Length; t += 3)
                {
                    int a = tr[t], b = tr[t + 1], c = tr[t + 2];
                    if ((uint)a >= v.Length || (uint)b >= v.Length || (uint)c >= v.Length) continue;
                    if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).LengthSquared() < 1e-16f) continue;
                    var bary = ClosestBary(points[i], v[a], v[b], v[c]);
                    float d = Vector3.DistanceSquared(points[i], v[a] * bary.X + v[b] * bary.Y + v[c] * bary.Z);
                    // Prefer visible skin only for essentially coincident surfaces.
                    if (d > best + 1e-10f || (MathF.Abs(d - best) <= 1e-10f && surface.Priority >= priority)) continue;
                    best = d; priority = surface.Priority; result[i] = new(s, a, b, c, bary);
                }
            }
            var q = result[i];
            if (!q.Valid) continue;
            var affected = surfaces[q.Surface].Affected;
            float weight = (affected[q.A] ? q.Bary.X : 0) + (affected[q.B] ? q.Bary.Y : 0) + (affected[q.C] ? q.Bary.Z : 0);
            if (weight <= 1e-6f) result[i] = new(-1, 0, 0, 0, default);
        }
        return result;
    }

    // Closest point on the whole triangle (including edges/corners), so a
    // coarse native collider never extrapolates beyond a body's surface patch.
    internal static Vector3 ClosestBary(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return new(1,0,0);
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
        if(d3>=0&&d4<=d3)return new(0,1,0);
        float vc=d1*d4-d3*d2;
        if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return new(1-v,v,0);}
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
        if(d6>=0&&d5<=d6)return new(0,0,1);
        float vb=d5*d2-d1*d6;
        if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return new(1-w,0,w);}
        float va=d3*d6-d5*d4;
        if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0){float w=(d4-d3)/((d4-d3)+(d5-d6));return new(0,1-w,w);}
        float inverse=1/(va+vb+vc);float y=vb*inverse,z=vc*inverse;return new(1-y-z,y,z);
    }
}
