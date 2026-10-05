using System.Numerics;

namespace ALPregnancy;

// Horn's absolute-orientation fit. Bone origins define the mesh coordinate
// conversion even when the two rigs use different local bone axis conventions.
internal static class RestPoseFit
{
    internal static bool TryFit(Vector3[] source, Vector3[] target, out Matrix4x4 map, out float error)
    {
        map = Matrix4x4.Identity;
        error = float.PositiveInfinity;
        if (source.Length != target.Length || source.Length < 3 || !HasPlane(source) || !HasPlane(target)) return false;
        Vector3 sc = Vector3.Zero, tc = Vector3.Zero;
        foreach (Vector3 p in source) sc += p;
        foreach (Vector3 p in target) tc += p;
        sc /= source.Length; tc /= target.Length;
        var s = new double[3, 3];
        double energy = 0;
        for (int i = 0; i < source.Length; i++)
        {
            Vector3 x = source[i] - sc, y = target[i] - tc;
            double[] a = { x.X, x.Y, x.Z }, b = { y.X, y.Y, y.Z };
            energy += x.LengthSquared();
            for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) s[j, k] += a[j] * b[k];
        }
        if (energy < 1e-16) return false;
        double trace = s[0, 0] + s[1, 1] + s[2, 2];
        var n = new double[4, 4]
        {
            {trace, s[1,2]-s[2,1], s[2,0]-s[0,2], s[0,1]-s[1,0]},
            {s[1,2]-s[2,1], s[0,0]-s[1,1]-s[2,2], s[0,1]+s[1,0], s[2,0]+s[0,2]},
            {s[2,0]-s[0,2], s[0,1]+s[1,0], -s[0,0]+s[1,1]-s[2,2], s[1,2]+s[2,1]},
            {s[0,1]-s[1,0], s[2,0]+s[0,2], s[1,2]+s[2,1], -s[0,0]-s[1,1]+s[2,2]}
        };
        var eigen = new double[4, 4];
        for (int i = 0; i < 4; i++) eigen[i, i] = 1;
        for (int iteration = 0; iteration < 64; iteration++)
        {
            int p = 0, q = 1;
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++)
                if (Math.Abs(n[i,j]) > Math.Abs(n[p,q])) { p = i; q = j; }
            if (Math.Abs(n[p,q]) < 1e-14 * energy) break;
            double tau = (n[q,q] - n[p,p]) / (2 * n[p,q]);
            double t = (tau >= 0 ? 1 : -1) / (Math.Abs(tau) + Math.Sqrt(1 + tau*tau));
            double c = 1 / Math.Sqrt(1+t*t), sn = t*c;
            n[p,p] -= t*n[p,q]; n[q,q] += t*n[p,q]; n[p,q] = n[q,p] = 0;
            for (int k = 0; k < 4; k++)
            {
                if (k != p && k != q)
                {
                    double a = n[k,p], b = n[k,q];
                    n[k,p] = n[p,k] = c*a-sn*b;
                    n[k,q] = n[q,k] = sn*a+c*b;
                }
                double ep = eigen[k,p], eq = eigen[k,q];
                eigen[k,p] = c*ep-sn*eq; eigen[k,q] = sn*ep+c*eq;
            }
        }
        int largest = 0;
        for (int i = 1; i < 4; i++) if (n[i,i] > n[largest,largest]) largest = i;
        Quaternion rotation = Quaternion.Normalize(new Quaternion((float)eigen[1,largest], (float)eigen[2,largest], (float)eigen[3,largest], (float)eigen[0,largest]));
        double numerator = 0;
        for (int i = 0; i < source.Length; i++) numerator += Vector3.Dot(Vector3.Transform(source[i] - sc, rotation), target[i] - tc);
        float scale = (float)(numerator / energy);
        if (!float.IsFinite(scale) || scale < 0.001f || scale > 1000f) return false;
        Vector3 translation = tc - Vector3.Transform(sc, rotation) * scale;
        map = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        error = 0;
        Vector3 min = target[0], max = target[0];
        for (int i = 0; i < source.Length; i++)
        {
            error = MathF.Max(error, Vector3.Distance(Vector3.Transform(source[i], map), target[i]));
            min = Vector3.Min(min, target[i]); max = Vector3.Max(max, target[i]);
        }
        return float.IsFinite(error) && error <= MathF.Max(1e-4f, Vector3.Distance(min, max) * 0.03f);
    }

    private static bool HasPlane(Vector3[] points)
    {
        Vector3 origin = points[0];
        int far = 0;
        for (int i = 1; i < points.Length; i++)
            if (Vector3.DistanceSquared(origin, points[i]) > Vector3.DistanceSquared(origin, points[far])) far = i;
        Vector3 axis = points[far] - origin;
        if (axis.LengthSquared() < 1e-12f) return false;
        foreach (Vector3 p in points)
            if (Vector3.Cross(axis, p-origin).LengthSquared() > axis.LengthSquared() * axis.LengthSquared() * 1e-6f) return true;
        return false;
    }
}
