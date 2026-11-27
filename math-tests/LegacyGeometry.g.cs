using UnityEngine;
namespace ALPregnancy;
internal static partial class LegacyGeometry {
private class BodyAnchorContext
        {
            public readonly List<BodyAnchorMesh> Meshes = new();
            public readonly List<BodyAnchorPoint> Points = new();
            public readonly Dictionary<int, List<int>> Buckets = new();
            public readonly List<BodySurfaceTriangle> SurfaceTriangles = new();
            public readonly Dictionary<int, List<int>> SurfaceBuckets = new();
            public float CellSize;
            public float MinMovedSq;
            public int BasisDirect;
            public int BasisSecondOrder;
            public int BasisFailed;
            public int ClothQueries;
            public int ClothHits;
            public int ClothMisses;
            public int ClothRejected;
        }
private class BodyAnchorMesh
        {
            public Vector3[] Original;
            public Vector3[] Morphed;
            public bool[] Valid;
            public bool[] Affected;
            public bool[] BreastExcluded;
            public float[] ProtectionKeep;
            public BodyAnchorBasis[] Bases;
            public List<int>[] SurfaceTrianglesByVertex;
        }
private struct BodyAnchorPoint
        {
            public int MeshIndex;
            public int VertexIndex;
            public Vector3 Original;
            public float OriginalUp;
        }
private struct BodyAnchorBasis
        {
            public bool Valid;
            public int Neighbor1;
            public int Neighbor2;
            public Matrix4x4 OriginalInverse;
        }
private struct BodySurfaceTriangle
        {
            public int MeshIndex;
            public int A;
            public int B;
            public int C;
            public Vector3 Center;
            public float RadiusSq;
        }
private struct BodySurfaceHit
        {
            public int TriangleIndex;
            public Vector3 Closest;
            public Vector3 Barycentric;
            public Vector3 Normal;
            public float DistanceSq;
        }
private struct LocalFrame
        {
            public TorsoProfile Profile;
            public Vector3 Center, Up, Fwd, Right;
            public float   BoneLen;
            public bool    BindPoseBased;
        }
private static void FinalizeBodyAnchorContext(BodyAnchorContext context)
        {
            if (context == null || context.Points.Count == 0) return;

            context.Points.Sort((a, b) => b.OriginalUp.CompareTo(a.OriginalUp));
            context.Buckets.Clear();
            for (int i = 0; i < context.Points.Count; i++)
            {
                var point = context.Points[i];
                BodyAnchorBucketCoords(point.Original, context.CellSize, out int bx, out int by, out int bz);
                int key = BodyAnchorBucketKey(bx, by, bz);
                if (!context.Buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    context.Buckets[key] = list;
                }
                list.Add(i);
            }
        }
private static bool TryGetBodySurfaceClothTarget(
            BodyAnchorContext context,
            Vector3 original,
            float clothMultiplier,
            LocalFrame fr,
            out Vector3 target)
        {
            target = original;
            if (context == null || context.Meshes.Count == 0 || context.SurfaceTriangles.Count == 0)
                return false;

            context.ClothQueries++;
            if (!TryFindNearestBodySurfaceTriangle(context, original, out BodySurfaceHit hit))
                return BodyAnchorMiss(context);

            float maxDistance = Mathf.Max(fr.BoneLen * 4.0f, 0.035f);
            if (hit.DistanceSq > maxDistance * maxDistance)
                return BodyAnchorMiss(context);

            if (hit.TriangleIndex < 0 || hit.TriangleIndex >= context.SurfaceTriangles.Count)
                return BodyAnchorMiss(context);

            BodySurfaceTriangle tri = context.SurfaceTriangles[hit.TriangleIndex];
            if (tri.MeshIndex < 0 || tri.MeshIndex >= context.Meshes.Count)
                return BodyAnchorMiss(context);

            BodyAnchorMesh mesh = context.Meshes[tri.MeshIndex];
            if (mesh == null || mesh.Original == null || mesh.Morphed == null)
                return BodyAnchorMiss(context);
            if (!IsValidBodyAnchorVertex(mesh, tri.A)
                || !IsValidBodyAnchorVertex(mesh, tri.B)
                || !IsValidBodyAnchorVertex(mesh, tri.C))
                return BodyAnchorMiss(context);

            float mul = clothMultiplier;
            Vector3 a = mesh.Original[tri.A];
            Vector3 b = mesh.Original[tri.B];
            Vector3 c = mesh.Original[tri.C];
            Vector3 am = ScaleAnchorMove(a, mesh.Morphed[tri.A], mul);
            Vector3 bm = ScaleAnchorMove(b, mesh.Morphed[tri.B], mul);
            Vector3 cm = ScaleAnchorMove(c, mesh.Morphed[tri.C], mul);

            Vector3 normalM = Vector3.Cross(bm - am, cm - am);
            float normalMLen = normalM.magnitude;
            if (normalMLen < 1e-9f || !IsFinite(normalM))
                return BodyAnchorMiss(context);
            normalM /= normalMLen;

            if (Vector3.Dot(hit.Normal, normalM) < -0.25f)
                return BodyAnchorMiss(context);

            Vector3 surfaceM =
                am * hit.Barycentric.x +
                bm * hit.Barycentric.y +
                cm * hit.Barycentric.z;
            var offset = original - hit.Closest;
            var rotated = SurfaceAttachment.RotateOffset(
                new System.Numerics.Vector3(offset.x, offset.y, offset.z),
                new System.Numerics.Vector3(hit.Normal.x, hit.Normal.y, hit.Normal.z),
                new System.Numerics.Vector3(normalM.x, normalM.y, normalM.z));
            target = surfaceM + new Vector3(rotated.X, rotated.Y, rotated.Z);
            float distanceFade = 1f - BellyShape.Smooth((Mathf.Sqrt(hit.DistanceSq) / maxDistance - 0.75f) / 0.25f);
            target = Vector3.Lerp(original, target, distanceFade);
            if (!IsFinite(target))
                return BodyAnchorMiss(context);

            context.ClothHits++;
            return true;
        }
private static bool TryFindNearestBodySurfaceTriangle(
            BodyAnchorContext context,
            Vector3 point,
            out BodySurfaceHit nearest)
        {
            nearest = new BodySurfaceHit { TriangleIndex = -1 };
            if (context == null || context.SurfaceTriangles.Count == 0)
                return false;

            bool found = false;
            float bestSq = float.MaxValue;

            if (!TryFindNearestBodyAnchorPoint(context, point, true, out BodyAnchorPoint anchor))
                return false;

            return TestBodySurfaceTrianglesForVertex(context, point, anchor, ref found, ref bestSq, ref nearest);
        }
private static bool TestBodySurfaceTrianglesForVertex(
            BodyAnchorContext context,
            Vector3 point,
            BodyAnchorPoint anchor,
            ref bool found,
            ref float bestSq,
            ref BodySurfaceHit nearest)
        {
            if (context == null || anchor.MeshIndex < 0 || anchor.MeshIndex >= context.Meshes.Count)
                return false;
            BodyAnchorMesh mesh = context.Meshes[anchor.MeshIndex];
            if (mesh?.SurfaceTrianglesByVertex == null)
                return false;
            if (anchor.VertexIndex < 0 || anchor.VertexIndex >= mesh.SurfaceTrianglesByVertex.Length)
                return false;

            List<int> triangles = mesh.SurfaceTrianglesByVertex[anchor.VertexIndex];
            if (triangles == null || triangles.Count == 0)
                return false;

            for (int i = 0; i < triangles.Count; i++)
                TestBodySurfaceTriangle(context, point, triangles[i], ref found, ref bestSq, ref nearest);
            return found;
        }
private static void TestBodySurfaceTriangle(
            BodyAnchorContext context,
            Vector3 point,
            int triangleIndex,
            ref bool found,
            ref float bestSq,
            ref BodySurfaceHit nearest)
        {
            if (context == null || triangleIndex < 0 || triangleIndex >= context.SurfaceTriangles.Count)
                return;

            BodySurfaceTriangle tri = context.SurfaceTriangles[triangleIndex];
            if (tri.MeshIndex < 0 || tri.MeshIndex >= context.Meshes.Count)
                return;

            BodyAnchorMesh mesh = context.Meshes[tri.MeshIndex];
            if (mesh == null || mesh.Original == null)
                return;
            if (!IsValidBodyAnchorVertex(mesh, tri.A)
                || !IsValidBodyAnchorVertex(mesh, tri.B)
                || !IsValidBodyAnchorVertex(mesh, tri.C))
                return;

            Vector3 a = mesh.Original[tri.A];
            Vector3 b = mesh.Original[tri.B];
            Vector3 c = mesh.Original[tri.C];
            if (!TryClosestPointOnTriangle(point, a, b, c, out Vector3 closest, out Vector3 barycentric))
                return;

            float sq = (point - closest).sqrMagnitude;
            if (sq >= bestSq)
                return;

            Vector3 normal = Vector3.Cross(b - a, c - a);
            float normalLen = normal.magnitude;
            if (normalLen < 1e-9f || !IsFinite(normal))
                return;
            normal /= normalLen;

            found = true;
            bestSq = sq;
            nearest = new BodySurfaceHit
            {
                TriangleIndex = triangleIndex,
                Closest = closest,
                Barycentric = barycentric,
                Normal = normal,
                DistanceSq = sq,
            };
        }
private static bool TryClosestPointOnTriangle(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out Vector3 closest,
            out Vector3 barycentric)
        {
            closest = a;
            barycentric = new Vector3(1f, 0f, 0f);

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return IsFinite(closest);

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                closest = b;
                barycentric = new Vector3(0f, 1f, 0f);
                return IsFinite(closest);
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float denom = d1 - d3;
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float v = d1 / denom;
                closest = a + ab * v;
                barycentric = new Vector3(1f - v, v, 0f);
                return IsFinite(closest);
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                closest = c;
                barycentric = new Vector3(0f, 0f, 1f);
                return IsFinite(closest);
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float denom = d2 - d6;
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float w = d2 / denom;
                closest = a + ac * w;
                barycentric = new Vector3(1f - w, 0f, w);
                return IsFinite(closest);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float denom = (d4 - d3) + (d5 - d6);
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float w = (d4 - d3) / denom;
                closest = b + (c - b) * w;
                barycentric = new Vector3(0f, 1f - w, w);
                return IsFinite(closest);
            }

            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < 1e-9f)
                return false;

            float inv = 1f / sum;
            float vFace = vb * inv;
            float wFace = vc * inv;
            float uFace = 1f - vFace - wFace;
            closest = a * uFace + b * vFace + c * wFace;
            barycentric = new Vector3(uFace, vFace, wFace);
            return IsFinite(closest) && IsFinite(barycentric);
        }
private static bool BodyAnchorMiss(BodyAnchorContext context)
        {
            if (context != null) context.ClothMisses++;
            return false;
        }
private static Vector3 ScaleAnchorMove(Vector3 original, Vector3 morphed, float multiplier)
        {
            return original + (morphed - original) * multiplier;
        }
private static bool IsValidBodyAnchorVertex(BodyAnchorMesh mesh, int index)
        {
            return mesh != null
                && mesh.Valid != null
                && index >= 0
                && index < mesh.Valid.Length
                && mesh.Valid[index];
        }
private static bool TryFindNearestBodyAnchorPoint(BodyAnchorContext context, Vector3 point, out BodyAnchorPoint nearest)
        {
            return TryFindNearestBodyAnchorPoint(context, point, false, out nearest);
        }
private static bool TryFindNearestBodyAnchorPoint(BodyAnchorContext context, Vector3 point, bool requireAffected, out BodyAnchorPoint nearest)
        {
            nearest = new BodyAnchorPoint();
            if (context == null || context.Points.Count == 0)
                return false;

            BodyAnchorBucketCoords(point, context.CellSize, out int bx, out int by, out int bz);
            bool found = false;
            float bestSq = float.MaxValue;

            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        int key = BodyAnchorBucketKey(bx + dx, by + dy, bz + dz);
                        if (!context.Buckets.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                            TestBodyAnchorPoint(context, point, list[i], requireAffected, ref found, ref bestSq, ref nearest);
                    }

            if (found) return true;

            for (int i = 0; i < context.Points.Count; i++)
                TestBodyAnchorPoint(context, point, i, requireAffected, ref found, ref bestSq, ref nearest);
            return found;
        }
private static void TestBodyAnchorPoint(
            BodyAnchorContext context,
            Vector3 point,
            int pointIndex,
            bool requireAffected,
            ref bool found,
            ref float bestSq,
            ref BodyAnchorPoint nearest)
        {
            if (context == null || pointIndex < 0 || pointIndex >= context.Points.Count) return;
            BodyAnchorPoint candidate = context.Points[pointIndex];
            if (requireAffected)
            {
                if (candidate.MeshIndex < 0 || candidate.MeshIndex >= context.Meshes.Count) return;
                BodyAnchorMesh mesh = context.Meshes[candidate.MeshIndex];
                if (mesh?.Affected == null || candidate.VertexIndex < 0 || candidate.VertexIndex >= mesh.Affected.Length) return;
                if (!mesh.Affected[candidate.VertexIndex]) return;
            }

            float sq = (candidate.Original - point).sqrMagnitude;
            if (sq >= bestSq) return;

            found = true;
            bestSq = sq;
            nearest = candidate;
        }
private static void BodyAnchorBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = Mathf.Max(cellSize, 0.0001f);
            x = Mathf.FloorToInt(point.x / cell);
            y = Mathf.FloorToInt(point.y / cell);
            z = Mathf.FloorToInt(point.z / cell);
        }
private static int BodyAnchorBucketKey(int x, int y, int z)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }
private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }
private static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }
private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }
private static int RepairClothDistortion(
            Vector3[] original,
            Vector3[] morphed,
            bool[] moved,
            LocalFrame fr,
            List<int>[] topologyNeighbors)
        {
            try
            {
                var p = BellyDeformSettings.Vtx;
                float detectThreshold = p.ClothDistortThreshold;
                float normalNeighborDiff = p.ClothDistortNeighborDiff;
                if (detectThreshold <= 0f || normalNeighborDiff < 0f) return 0;
                if (original == null || morphed == null || moved == null) return 0;
                int count = original.Length;
                if (morphed.Length != count || moved.Length != count) return 0;
                if (topologyNeighbors == null || topologyNeighbors.Length != count) return 0;

                float boneLen = Mathf.Max(fr.BoneLen, 1e-5f);
                float minMoveSq = boneLen * boneLen * 1e-8f;
                var movedIndices = new List<int>();

                for (int i = 0; i < count; i++)
                {
                    if (!moved[i]) continue;
                    Vector3 disp = morphed[i] - original[i];
                    if (disp.sqrMagnitude <= minMoveSq) continue;

                    movedIndices.Add(i);
                }

                if (movedIndices.Count < 3) return 0;
                float radius = Mathf.Max(boneLen * 1.50f, 0.012f);
                float radiusSq = radius * radius;
                float cell = Mathf.Max(radius, 0.001f);
                var buckets = BuildSpatialBuckets(original, movedIndices, cell);
                if (buckets.Count == 0) return 0;

                var replacement = new Vector3[count];
                var shouldReplace = new bool[count];
                var neighbors = new List<int>(48);
                int fixedCount = 0;
                int checkedCount = 0;
                int noNeighborCount = 0;
                int noSampleCount = 0;
                float maxDiffNorm = 0f;
                float maxEdgeDiffNorm = 0f;

                for (int m = 0; m < movedIndices.Count; m++)
                {
                    int i = movedIndices[m];
                    List<int> topo = topologyNeighbors[i];
                    if (topo == null || topo.Count == 0)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 dispI = morphed[i] - original[i];
                    Vector3 topoSum = Vector3.zero;
                    int topoSamples = 0;
                    int tornEdges = 0;
                    float localMaxEdgeDiffNorm = 0f;

                    for (int t = 0; t < topo.Count; t++)
                    {
                        int j = topo[t];
                        if (j == i || j < 0 || j >= count || !moved[j]) continue;
                        Vector3 dispJ = morphed[j] - original[j];
                        if (dispJ.sqrMagnitude <= minMoveSq) continue;

                        float edgeDiffNorm = (dispI - dispJ).magnitude / boneLen;
                        if (edgeDiffNorm > localMaxEdgeDiffNorm)
                            localMaxEdgeDiffNorm = edgeDiffNorm;
                        if (edgeDiffNorm >= detectThreshold)
                            tornEdges++;

                        topoSum += dispJ;
                        topoSamples++;
                    }

                    if (localMaxEdgeDiffNorm > maxEdgeDiffNorm)
                        maxEdgeDiffNorm = localMaxEdgeDiffNorm;
                    if (topoSamples < 2 || tornEdges == 0)
                    {
                        if (topoSamples < 2) noNeighborCount++;
                        continue;
                    }

                    neighbors.Clear();
                    CollectSpatialNeighbors(original, buckets, cell, radiusSq, i, neighbors);
                    if (neighbors.Count == 0)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 localSum = topoSum;
                    int localSamples = topoSamples;
                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int j = neighbors[n];
                        if (j == i || !moved[j]) continue;

                        Vector3 dispJ = morphed[j] - original[j];
                        if (dispJ.sqrMagnitude <= minMoveSq) continue;
                        localSum += dispJ;
                        localSamples++;
                    }

                    if (localSamples < 2)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 localAvg = localSum / localSamples;
                    float diffNorm = (dispI - localAvg).magnitude / boneLen;
                    if (diffNorm > maxDiffNorm)
                        maxDiffNorm = diffNorm;
                    checkedCount++;

                    if (diffNorm < detectThreshold) continue;

                    Vector3 sum = Vector3.zero;
                    int samples = 0;
                    for (int t = 0; t < topo.Count; t++)
                    {
                        int k = topo[t];
                        if (k == i || k < 0 || k >= count || !moved[k]) continue;

                        Vector3 kDisp = morphed[k] - original[k];
                        if (kDisp.sqrMagnitude <= minMoveSq) continue;

                        float sampleDiff = (kDisp - localAvg).magnitude / boneLen;
                        if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                        {
                            sum += kDisp;
                            samples++;
                        }
                    }

                    if (samples == 0)
                    {
                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            int k = neighbors[n];
                            if (k == i || !moved[k]) continue;

                            Vector3 kDisp = morphed[k] - original[k];
                            if (kDisp.sqrMagnitude <= minMoveSq) continue;

                            float sampleDiff = (kDisp - localAvg).magnitude / boneLen;
                            if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                            {
                                sum += kDisp;
                                samples++;
                            }
                        }
                    }

                    Vector3 replacementDisp;
                    if (samples > 0)
                    {
                        replacementDisp = sum / samples;
                    }
                    else
                    {
                        noSampleCount++;
                        if (tornEdges < 2 || localMaxEdgeDiffNorm < detectThreshold * 2f)
                            continue;
                        replacementDisp = topoSum / topoSamples;
                    }

                    replacement[i] = original[i] + replacementDisp;
                    shouldReplace[i] = true;
                    fixedCount++;
                }

                if (checkedCount > 0 && (fixedCount > 0 || maxDiffNorm >= detectThreshold * 0.75f || maxEdgeDiffNorm >= detectThreshold))
                    RuntimeLogInfo($"[VtxMorph] ClothDistortFix: fixed={fixedCount}/{movedIndices.Count} checked={checkedCount} noNbr={noNeighborCount} noSample={noSampleCount} maxDiff={maxDiffNorm:F2} maxEdge={maxEdgeDiffNorm:F2} thr={detectThreshold:F2} near={normalNeighborDiff:F2}");

                if (fixedCount == 0) return 0;
                for (int i = 0; i < count; i++)
                    if (shouldReplace[i])
                        morphed[i] = replacement[i];
                return fixedCount;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ClothDistortFix: " + e.Message);
                return 0;
            }
        }
private static Dictionary<int, List<int>> BuildSpatialBuckets(Vector3[] points, List<int> indices, float cellSize)
        {
            var buckets = new Dictionary<int, List<int>>();
            if (points == null || indices == null) return buckets;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= points.Length) continue;
                SpatialBucketCoords(points[index], cellSize, out int x, out int y, out int z);
                int key = SpatialBucketKey(x, y, z);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(8);
                    buckets[key] = list;
                }
                list.Add(index);
            }
            return buckets;
        }
private static void CollectSpatialNeighbors(
            Vector3[] points,
            Dictionary<int, List<int>> buckets,
            float cellSize,
            float radiusSq,
            int centerIndex,
            List<int> result)
        {
            if (points == null || buckets == null || result == null) return;
            if (centerIndex < 0 || centerIndex >= points.Length) return;

            Vector3 center = points[centerIndex];
            SpatialBucketCoords(center, cellSize, out int bx, out int by, out int bz);
            int range = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(radiusSq) / Mathf.Max(cellSize, 1e-5f)));

            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                    for (int dz = -range; dz <= range; dz++)
                    {
                        int key = SpatialBucketKey(bx + dx, by + dy, bz + dz);
                        if (!buckets.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            int candidate = list[i];
                            if (candidate == centerIndex) continue;
                            if (candidate < 0 || candidate >= points.Length) continue;
                            if ((points[candidate] - center).sqrMagnitude <= radiusSq)
                                result.Add(candidate);
                        }
                    }
        }
private static void SpatialBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = Mathf.Max(cellSize, 0.0001f);
            x = Mathf.FloorToInt(point.x / cell);
            y = Mathf.FloorToInt(point.y / cell);
            z = Mathf.FloorToInt(point.z / cell);
        }
private static int SpatialBucketKey(int x, int y, int z)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }
private static int[] BuildNearestBodyVertCache(Vector3[] overlayVerts, Vector3[] bodyVerts)
        {
            int on = overlayVerts.Length;
            int bn = bodyVerts.Length;
            var cache = new int[on];
            for (int i = 0; i < on; i++)
            {
                Vector3 op = overlayVerts[i];
                float bestSq = float.MaxValue;
                int best = 0;
                for (int j = 0; j < bn; j++)
                {
                    float dSq = (bodyVerts[j] - op).sqrMagnitude;
                    if (dSq < bestSq) { bestSq = dSq; best = j; }
                }
                cache[i] = best;
            }
            return cache;
        }
private static int[] ComputeNormalWeldGroup(Vector3[] verts)
        {
            int n = verts.Length;
            var map = new Dictionary<(int, int, int), int>(n);
            var group = new int[n];
            const float invEps = 1000f; // 1 mm precision
            for (int i = 0; i < n; i++)
            {
                Vector3 v = verts[i];
                var key = (Mathf.RoundToInt(v.x * invEps),
                           Mathf.RoundToInt(v.y * invEps),
                           Mathf.RoundToInt(v.z * invEps));
                if (!map.TryGetValue(key, out int rep))
                {
                    rep = i;
                    map[key] = i;
                }
                group[i] = rep;
            }
            return group;
        }
private static Vector3[] ComputeWeldedNormalsOffline(Mesh mesh, Vector3[] verts, int[] weldGroup)
        {
            if (weldGroup == null || verts == null || verts.Length != weldGroup.Length) return null;
            try
            {
                int[] tris = mesh.triangles;
                if (tris == null || tris.Length < 3) return null;
                int n = verts.Length;
                var accum = new Vector3[n];
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                    Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                    if (cross.sqrMagnitude < 1e-20f) continue;
                    accum[weldGroup[a]] += cross;
                    accum[weldGroup[b]] += cross;
                    accum[weldGroup[c]] += cross;
                }
                var normals = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 s = accum[weldGroup[i]];
                    normals[i] = s.sqrMagnitude > 1e-20f ? s.normalized : Vector3.up;
                }
                return normals;
            }
            catch { return null; }
        }
}
