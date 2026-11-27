using UnityEngine;

namespace ALPregnancy;

internal static partial class FastGeometry
{
    // Exact rest-point search. The old lookup prefers its 7x7x7 hashed bucket
    // neighborhood, then falls back to all points. A global nearest inside that
    // neighborhood is necessarily also its nearest; otherwise the caller keeps
    // the original neighborhood scan. Equal distances retain bucket/list order.
    private sealed class BodyAnchorSearch
    {
        private struct Node
        {
            internal int Point, Left, Right, Axis;
            internal bool Affected, AnyAffected;
        }
        private readonly BodyAnchorContext _context;
        private readonly Node[] _nodes;
        private readonly int[] _keys;
        private readonly int _root;
        private int _next;

        internal BodyAnchorSearch(BodyAnchorContext context)
        {
            _context = context;
            _nodes = new Node[context.Points.Count];
            _keys = new int[context.Points.Count];
            for (int i = 0; i < _keys.Length; i++)
            {
                BodyAnchorBucketCoords(context.Points[i].Original, context.CellSize, out int x, out int y, out int z);
                _keys[i] = BodyAnchorBucketKey(x, y, z);
            }
            var order = Enumerable.Range(0, context.Points.Count).ToArray();
            _root = Build(order, 0, order.Length, 0);
        }

        private int Build(int[] order, int start, int count, int axis)
        {
            if (count == 0) return -1;
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
            {
                int c = Coordinate(_context.Points[a].Original, axis).CompareTo(Coordinate(_context.Points[b].Original, axis));
                return c == 0 ? a.CompareTo(b) : c;
            }));
            int middle = start + count / 2, slot = _next++;
            var point = _context.Points[order[middle]];
            bool affected = _context.Meshes[point.MeshIndex].Affected[point.VertexIndex];
            int left = Build(order, start, middle - start, (axis + 1) % 3);
            int right = Build(order, middle + 1, start + count - middle - 1, (axis + 1) % 3);
            _nodes[slot] = new Node { Point = order[middle], Left = left, Right = right, Axis = axis,
                Affected = affected, AnyAffected = affected || left >= 0 && _nodes[left].AnyAffected || right >= 0 && _nodes[right].AnyAffected };
            return slot;
        }

        private static float Coordinate(Vector3 p, int axis) => axis == 0 ? p.x : axis == 1 ? p.y : p.z;

        // A hash can alias a different cell. Evaluate the same key sequence as
        // the old lookup, including aliases and its first-encounter tie rule.
        internal int BucketRank(int point, int bx, int by, int bz)
        {
            // BodyAnchorBucketKey is a base-31 polynomial. Decode its exact
            // relative key, including int wraparound, instead of enumerating
            // 343 cells. The +/-3 digits have no ambiguous representations.
            int delta = unchecked(_keys[point] - BodyAnchorBucketKey(bx, by, bz));
            if (delta < -2979 || delta > 2979) return int.MaxValue;
            int dx = (delta + (delta >= 0 ? 480 : -480)) / 961;
            int rest = delta - dx * 961;
            int dy = (rest + (rest >= 0 ? 15 : -15)) / 31;
            int dz = rest - dy * 31;
            if (dx < -3 || dx > 3 || dy < -3 || dy > 3 || dz < -3 || dz > 3) return int.MaxValue;
            return ((dx + 3) * 7 + dy + 3) * 7 + dz + 3;
        }

        internal int Nearest(Vector3 point, bool affected, int bx, int by, int bz)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            Visit(_root, point, affected, bx, by, bz, ref best, ref bestSq);
            return best;
        }

        private void Visit(int slot, Vector3 point, bool affected, int bx, int by, int bz, ref int best, ref float bestSq)
        {
            if (slot < 0) return;
            var node = _nodes[slot];
            if (affected && !node.AnyAffected) return;
            var p = _context.Points[node.Point].Original;
            if (!affected || node.Affected)
            {
                float x = p.x - point.x, y = p.y - point.y, z = p.z - point.z;
                float sq = x * x + y * y + z * z;
                if (sq < bestSq || sq == bestSq && best >= 0 && Earlier(node.Point, best, bx, by, bz))
                { best = node.Point; bestSq = sq; }
            }
            float distance = Coordinate(point, node.Axis) - Coordinate(p, node.Axis);
            int near = distance <= 0 ? node.Left : node.Right, far = distance <= 0 ? node.Right : node.Left;
            Visit(near, point, affected, bx, by, bz, ref best, ref bestSq);
            if (distance * distance <= bestSq) Visit(far, point, affected, bx, by, bz, ref best, ref bestSq);
        }

        private bool Earlier(int a, int b, int bx, int by, int bz)
        {
            int ar = BucketRank(a, bx, by, bz), br = BucketRank(b, bx, by, bz);
            return ar < br || ar == br && a < b;
        }
    }
}
