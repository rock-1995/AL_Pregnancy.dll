using UnityEngine;
namespace ALPregnancy;
internal static partial class FastGeometry {
private class BodyAnchorContext
        {
            public BodyAnchorSearch Search;
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
            // Preserve the legacy fallback for malformed nonfinite source data.
            context.Search = context.Points.All(p => IsFinite(p.Original)) ? new BodyAnchorSearch(context) : null;
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

            float maxDistance = MorphMath.Max(fr.BoneLen * 4.0f,0.035f);
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

            Vector3 normalM = MorphMath.Cross(MorphMath.Subtract(bm,am),MorphMath.Subtract(cm,am));
            float normalMLen = MorphMath.Magnitude(normalM);
            if (normalMLen < 1e-9f || !IsFinite(normalM))
                return BodyAnchorMiss(context);
            normalM =MorphMath.Divide(normalM,normalMLen);

            if (MorphMath.Dot(hit.Normal,normalM) < -0.25f)
                return BodyAnchorMiss(context);

            Vector3 surfaceM =
                MorphMath.Add(MorphMath.Add(MorphMath.Multiply(am,hit.Barycentric.x),MorphMath.Multiply(bm,hit.Barycentric.y)),MorphMath.Multiply(cm,hit.Barycentric.z));
            var offset = MorphMath.Subtract(original,hit.Closest);
            var rotated = SurfaceAttachment.RotateOffset(
                new System.Numerics.Vector3(offset.x, offset.y, offset.z),
                new System.Numerics.Vector3(hit.Normal.x, hit.Normal.y, hit.Normal.z),
                new System.Numerics.Vector3(normalM.x, normalM.y, normalM.z));
            target = MorphMath.Add(surfaceM,MorphMath.V(rotated.X,rotated.Y,rotated.Z));
            float distanceFade = 1f - BellyShape.Smooth((MorphMath.Sqrt(hit.DistanceSq) / maxDistance - 0.75f) / 0.25f);
            target = MorphMath.Lerp(original,target,distanceFade);
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

            float sq = MorphMath.SqrMagnitude((MorphMath.Subtract(point,closest)));
            if (sq >= bestSq)
                return;

            Vector3 normal = MorphMath.Cross(MorphMath.Subtract(b,a),MorphMath.Subtract(c,a));
            float normalLen = MorphMath.Magnitude(normal);
            if (normalLen < 1e-9f || !IsFinite(normal))
                return;
            normal =MorphMath.Divide(normal,normalLen);

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
            barycentric = MorphMath.V(1f,0f,0f);

            Vector3 ab = MorphMath.Subtract(b,a);
            Vector3 ac = MorphMath.Subtract(c,a);
            Vector3 ap = MorphMath.Subtract(point,a);
            float d1 = MorphMath.Dot(ab,ap);
            float d2 = MorphMath.Dot(ac,ap);
            if (d1 <= 0f && d2 <= 0f)
                return IsFinite(closest);

            Vector3 bp = MorphMath.Subtract(point,b);
            float d3 = MorphMath.Dot(ab,bp);
            float d4 = MorphMath.Dot(ac,bp);
            if (d3 >= 0f && d4 <= d3)
            {
                closest = b;
                barycentric = MorphMath.V(0f,1f,0f);
                return IsFinite(closest);
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float denom = d1 - d3;
                if (MorphMath.Abs(denom) < 1e-9f) return false;
                float v = d1 / denom;
                closest = MorphMath.Add(a,MorphMath.Multiply(ab,v));
                barycentric = MorphMath.V(1f - v,v,0f);
                return IsFinite(closest);
            }

            Vector3 cp = MorphMath.Subtract(point,c);
            float d5 = MorphMath.Dot(ab,cp);
            float d6 = MorphMath.Dot(ac,cp);
            if (d6 >= 0f && d5 <= d6)
            {
                closest = c;
                barycentric = MorphMath.V(0f,0f,1f);
                return IsFinite(closest);
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float denom = d2 - d6;
                if (MorphMath.Abs(denom) < 1e-9f) return false;
                float w = d2 / denom;
                closest = MorphMath.Add(a,MorphMath.Multiply(ac,w));
                barycentric = MorphMath.V(1f - w,0f,w);
                return IsFinite(closest);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float denom = (d4 - d3) + (d5 - d6);
                if (MorphMath.Abs(denom) < 1e-9f) return false;
                float w = (d4 - d3) / denom;
                closest = MorphMath.Add(b,MorphMath.Multiply((MorphMath.Subtract(c,b)),w));
                barycentric = MorphMath.V(0f,1f - w,w);
                return IsFinite(closest);
            }

            float sum = va + vb + vc;
            if (MorphMath.Abs(sum) < 1e-9f)
                return false;

            float inv = 1f / sum;
            float vFace = vb * inv;
            float wFace = vc * inv;
            float uFace = 1f - vFace - wFace;
            closest = MorphMath.Add(MorphMath.Add(MorphMath.Multiply(a,uFace),MorphMath.Multiply(b,vFace)),MorphMath.Multiply(c,wFace));
            barycentric = MorphMath.V(uFace,vFace,wFace);
            return IsFinite(closest) && IsFinite(barycentric);
        }
private static bool BodyAnchorMiss(BodyAnchorContext context)
        {
            if (context != null) context.ClothMisses++;
            return false;
        }
private static Vector3 ScaleAnchorMove(Vector3 original, Vector3 morphed, float multiplier)
        {
            return MorphMath.Add(original,MorphMath.Multiply((MorphMath.Subtract(morphed,original)),multiplier));
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

            bool indexed = context.Search != null && IsFinite(point);
            int global = indexed ? context.Search.Nearest(point, requireAffected, bx, by, bz) : -1;
            if (indexed && global < 0) return false;
            if (indexed && context.Search.BucketRank(global, bx, by, bz) != int.MaxValue)
            {
                nearest = context.Points[global];
                return true;
            }

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

            if (indexed) { nearest = context.Points[global]; return true; }
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

            float sq = MorphMath.SqrMagnitude((MorphMath.Subtract(candidate.Original,point)));
            if (sq >= bestSq) return;

            found = true;
            bestSq = sq;
            nearest = candidate;
        }
private static void BodyAnchorBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = MorphMath.Max(cellSize,0.0001f);
            x = MorphMath.FloorToInt(point.x / cell);
            y = MorphMath.FloorToInt(point.y / cell);
            z = MorphMath.FloorToInt(point.z / cell);
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

                float boneLen = MorphMath.Max(fr.BoneLen,1e-5f);
                float minMoveSq = boneLen * boneLen * 1e-8f;
                var movedIndices = new List<int>();

                for (int i = 0; i < count; i++)
                {
                    if (!moved[i]) continue;
                    Vector3 disp = MorphMath.Subtract(morphed[i],original[i]);
                    if (MorphMath.SqrMagnitude(disp) <= minMoveSq) continue;

                    movedIndices.Add(i);
                }

                if (movedIndices.Count < 3) return 0;
                float radius = MorphMath.Max(boneLen * 1.50f,0.012f);
                float radiusSq = radius * radius;
                float cell = MorphMath.Max(radius,0.001f);
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

                    Vector3 dispI = MorphMath.Subtract(morphed[i],original[i]);
                    Vector3 topoSum = MorphMath.V(0,0,0);
                    int topoSamples = 0;
                    int tornEdges = 0;
                    float localMaxEdgeDiffNorm = 0f;

                    for (int t = 0; t < topo.Count; t++)
                    {
                        int j = topo[t];
                        if (j == i || j < 0 || j >= count || !moved[j]) continue;
                        Vector3 dispJ = MorphMath.Subtract(morphed[j],original[j]);
                        if (MorphMath.SqrMagnitude(dispJ) <= minMoveSq) continue;

                        float edgeDiffNorm = MorphMath.Magnitude((MorphMath.Subtract(dispI,dispJ))) / boneLen;
                        if (edgeDiffNorm > localMaxEdgeDiffNorm)
                            localMaxEdgeDiffNorm = edgeDiffNorm;
                        if (edgeDiffNorm >= detectThreshold)
                            tornEdges++;

                        topoSum =MorphMath.Add(topoSum,dispJ);
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

                        Vector3 dispJ = MorphMath.Subtract(morphed[j],original[j]);
                        if (MorphMath.SqrMagnitude(dispJ) <= minMoveSq) continue;
                        localSum =MorphMath.Add(localSum,dispJ);
                        localSamples++;
                    }

                    if (localSamples < 2)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 localAvg = MorphMath.Divide(localSum,localSamples);
                    float diffNorm = MorphMath.Magnitude((MorphMath.Subtract(dispI,localAvg))) / boneLen;
                    if (diffNorm > maxDiffNorm)
                        maxDiffNorm = diffNorm;
                    checkedCount++;

                    if (diffNorm < detectThreshold) continue;

                    Vector3 sum = MorphMath.V(0,0,0);
                    int samples = 0;
                    for (int t = 0; t < topo.Count; t++)
                    {
                        int k = topo[t];
                        if (k == i || k < 0 || k >= count || !moved[k]) continue;

                        Vector3 kDisp = MorphMath.Subtract(morphed[k],original[k]);
                        if (MorphMath.SqrMagnitude(kDisp) <= minMoveSq) continue;

                        float sampleDiff = MorphMath.Magnitude((MorphMath.Subtract(kDisp,localAvg))) / boneLen;
                        if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                        {
                            sum =MorphMath.Add(sum,kDisp);
                            samples++;
                        }
                    }

                    if (samples == 0)
                    {
                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            int k = neighbors[n];
                            if (k == i || !moved[k]) continue;

                            Vector3 kDisp = MorphMath.Subtract(morphed[k],original[k]);
                            if (MorphMath.SqrMagnitude(kDisp) <= minMoveSq) continue;

                            float sampleDiff = MorphMath.Magnitude((MorphMath.Subtract(kDisp,localAvg))) / boneLen;
                            if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                            {
                                sum =MorphMath.Add(sum,kDisp);
                                samples++;
                            }
                        }
                    }

                    Vector3 replacementDisp;
                    if (samples > 0)
                    {
                        replacementDisp = MorphMath.Divide(sum,samples);
                    }
                    else
                    {
                        noSampleCount++;
                        if (tornEdges < 2 || localMaxEdgeDiffNorm < detectThreshold * 2f)
                            continue;
                        replacementDisp = MorphMath.Divide(topoSum,topoSamples);
                    }

                    replacement[i] = MorphMath.Add(original[i],replacementDisp);
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
            int range = MorphMath.Max(1,MorphMath.CeilToInt(MorphMath.Sqrt(radiusSq) / MorphMath.Max(cellSize,1e-5f)));

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
                            if (MorphMath.SqrMagnitude((MorphMath.Subtract(points[candidate],center))) <= radiusSq)
                                result.Add(candidate);
                        }
                    }
        }
private static void SpatialBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = MorphMath.Max(cellSize,0.0001f);
            x = MorphMath.FloorToInt(point.x / cell);
            y = MorphMath.FloorToInt(point.y / cell);
            z = MorphMath.FloorToInt(point.z / cell);
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
                    float dSq = MorphMath.SqrMagnitude((MorphMath.Subtract(bodyVerts[j],op)));
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
                var key = (MorphMath.RoundToInt(v.x * invEps),
                           MorphMath.RoundToInt(v.y * invEps),
                           MorphMath.RoundToInt(v.z * invEps));
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
                    Vector3 cross = MorphMath.Cross(MorphMath.Subtract(verts[b],verts[a]),MorphMath.Subtract(verts[c],verts[a]));
                    if (MorphMath.SqrMagnitude(cross) < 1e-20f) continue;
                    accum[weldGroup[a]] =MorphMath.Add(accum[weldGroup[a]],cross);
                    accum[weldGroup[b]] =MorphMath.Add(accum[weldGroup[b]],cross);
                    accum[weldGroup[c]] =MorphMath.Add(accum[weldGroup[c]],cross);
                }
                var normals = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 s = accum[weldGroup[i]];
                    normals[i] = MorphMath.SqrMagnitude(s) > 1e-20f ? MorphMath.Normalize(s) : MorphMath.V(0,1,0);
                }
                return normals;
            }
            catch { return null; }
        }
}
